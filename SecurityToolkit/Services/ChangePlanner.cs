using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using SecurityToolkit.Models;

namespace SecurityToolkit.Services
{
    /// <summary>What a "clone user" run should copy from the source user to the target users.</summary>
    internal sealed class CloneOptions
    {
        public bool BusinessUnit { get; set; }
        public bool Roles { get; set; }
        public bool Teams { get; set; }

        /// <summary>Also remove roles and teams the target has that the source does not (make the target match the source).</summary>
        public bool RemoveExtras { get; set; }
    }

    /// <summary>
    /// Turns "give these users these roles" and similar requests into the exact list of changes to make, applying
    /// the rules Dataverse enforces: roles are business-unit specific, only some teams take manual members, and
    /// nothing is planned for what is already in place. Nothing here talks to Dataverse.
    /// </summary>
    internal static class ChangePlanner
    {
        public const string UserRoleRelationship = "systemuserroles_association";
        public const string TeamRoleRelationship = "teamroles_association";
        public const string TeamMembershipRelationship = "teammembership_association";

        private static readonly StringComparer Names = StringComparer.CurrentCultureIgnoreCase;

        /// <summary>Add (or remove) security roles to (from) users.</summary>
        public static ChangePlan PlanUserRoles(SecurityGraph graph, IEnumerable<UserInfo> users, IEnumerable<RoleChoice> roles, bool add)
        {
            var plan = new ChangePlan();
            var already = 0;
            var notHeld = 0;
            foreach (var user in users.OrderBy(u => u.FullName, Names))
            {
                var held = graph.DirectRolesOf(user.Id);
                foreach (var choice in roles.OrderBy(r => r.Name, Names))
                {
                    var current = held.Where(r => r.RootKey == choice.RootKey).ToList();
                    if (add)
                    {
                        if (current.Count > 0) { already++; continue; }

                        var copy = CopyIn(graph, choice, user.BusinessUnitId);
                        if (copy == null)
                        {
                            plan.Notes.Add($"{user.FullName}: role \"{choice.Name}\" has no copy in business unit \"{user.BusinessUnitName}\".");
                            continue;
                        }

                        plan.Items.Add(RoleLink("Add role", true, "systemuser", user.Id, user.FullName, UserRoleRelationship, copy));
                    }
                    else
                    {
                        if (current.Count == 0) { notHeld++; continue; }

                        foreach (var role in current)
                        {
                            plan.Items.Add(RoleLink("Remove role", false, "systemuser", user.Id, user.FullName, UserRoleRelationship, role));
                        }
                    }
                }
            }

            Summarize(plan, add, already, notHeld, "user");
            return plan;
        }

        /// <summary>Add (or remove) security roles to (from) teams.</summary>
        public static ChangePlan PlanTeamRoles(SecurityGraph graph, IEnumerable<TeamInfo> teams, IEnumerable<RoleChoice> roles, bool add)
        {
            var plan = new ChangePlan();
            var already = 0;
            var notHeld = 0;
            foreach (var team in teams.OrderBy(t => t.Name, Names))
            {
                if (!team.CanHoldRoles)
                {
                    plan.Notes.Add($"{team.Name}: access teams cannot have security roles.");
                    continue;
                }

                var held = graph.RolesOfTeam(team.Id);
                foreach (var choice in roles.OrderBy(r => r.Name, Names))
                {
                    var current = held.Where(r => r.RootKey == choice.RootKey).ToList();
                    if (add)
                    {
                        if (current.Count > 0) { already++; continue; }

                        var copy = CopyIn(graph, choice, team.BusinessUnitId);
                        if (copy == null)
                        {
                            plan.Notes.Add($"{team.Name}: role \"{choice.Name}\" has no copy in business unit \"{team.BusinessUnitName}\".");
                            continue;
                        }

                        plan.Items.Add(RoleLink("Add role", true, "team", team.Id, team.Name, TeamRoleRelationship, copy));
                    }
                    else
                    {
                        if (current.Count == 0) { notHeld++; continue; }

                        foreach (var role in current)
                        {
                            plan.Items.Add(RoleLink("Remove role", false, "team", team.Id, team.Name, TeamRoleRelationship, role));
                        }
                    }
                }
            }

            Summarize(plan, add, already, notHeld, "team");
            return plan;
        }

        /// <summary>Add (or remove) users to (from) teams. Only teams whose members are managed by hand are touched.</summary>
        public static ChangePlan PlanMembership(SecurityGraph graph, IEnumerable<UserInfo> users, IEnumerable<TeamInfo> teams, bool add)
        {
            var plan = new ChangePlan();
            var userList = users.OrderBy(u => u.FullName, Names).ToList();
            var already = 0;
            var notMember = 0;
            foreach (var team in teams.OrderBy(t => t.Name, Names))
            {
                if (!team.IsManuallyManaged)
                {
                    plan.Notes.Add($"{team.Name}: members of this {team.TypeName.ToLowerInvariant()} team{(team.IsDefault ? " (the business unit's default team)" : string.Empty)} are not managed by hand.");
                    continue;
                }

                var members = new HashSet<Guid>(graph.MembersOf(team.Id).Select(u => u.Id));
                foreach (var user in userList)
                {
                    if (members.Contains(user.Id) == add)
                    {
                        if (add) { already++; } else { notMember++; }
                        continue;
                    }

                    plan.Items.Add(Membership(add, user, team));
                }
            }

            Summarize(plan, add, already, notMember, "user");
            return plan;
        }

        /// <summary>Copy business unit, roles and teams from <paramref name="source"/> to each of <paramref name="targets"/>.</summary>
        public static ChangePlan PlanClone(SecurityGraph graph, UserInfo source, IEnumerable<UserInfo> targets, CloneOptions options)
        {
            var plan = new ChangePlan();
            var sourceRoles = graph.DirectRolesOf(source.Id);
            var sourceKeys = new HashSet<Guid>(sourceRoles.Select(r => r.RootKey));
            var sourceTeams = graph.TeamsOf(source.Id).Where(t => t.IsManuallyManaged).ToList();

            foreach (var target in targets.Where(t => t.Id != source.Id).OrderBy(t => t.FullName, Names))
            {
                var movesBusinessUnit = options.BusinessUnit && target.BusinessUnitId != source.BusinessUnitId;
                var finalBusinessUnit = options.BusinessUnit ? source.BusinessUnitId : target.BusinessUnitId;

                if (movesBusinessUnit)
                {
                    plan.Items.Add(new ChangeItem("Change business unit", target.FullName, source.BusinessUnitName,
                        new SetBusinessSystemUserRequest
                        {
                            UserId = target.Id,
                            BusinessId = source.BusinessUnitId.GetValueOrDefault(),
                            // The user's own records stay with them.
                            ReassignPrincipal = new EntityReference("systemuser", target.Id)
                        }));
                    plan.Notes.Add($"{target.FullName}: moving to \"{source.BusinessUnitName}\" removes all their current security roles, and this step cannot be undone by the toolkit.");
                }

                if (options.Roles)
                {
                    // A business unit change leaves the user with no roles at all.
                    var held = movesBusinessUnit ? new List<RoleInfo>() : graph.DirectRolesOf(target.Id).ToList();
                    var heldKeys = new HashSet<Guid>(held.Select(r => r.RootKey));

                    foreach (var role in sourceRoles)
                    {
                        if (heldKeys.Contains(role.RootKey))
                        {
                            continue;
                        }

                        var copy = graph.Roles.FirstOrDefault(r => r.RootKey == role.RootKey && r.BusinessUnitId == finalBusinessUnit);
                        if (copy == null)
                        {
                            plan.Notes.Add($"{target.FullName}: role \"{role.Name}\" has no copy in the user's business unit.");
                            continue;
                        }

                        plan.Items.Add(RoleLink("Add role", true, "systemuser", target.Id, target.FullName, UserRoleRelationship, copy));
                    }

                    if (options.RemoveExtras)
                    {
                        foreach (var role in held.Where(r => !sourceKeys.Contains(r.RootKey)))
                        {
                            plan.Items.Add(RoleLink("Remove role", false, "systemuser", target.Id, target.FullName, UserRoleRelationship, role));
                        }
                    }
                }

                if (options.Teams)
                {
                    var current = graph.TeamsOf(target.Id).Where(t => t.IsManuallyManaged).ToList();
                    var currentIds = new HashSet<Guid>(current.Select(t => t.Id));
                    var wantedIds = new HashSet<Guid>(sourceTeams.Select(t => t.Id));

                    foreach (var team in sourceTeams.Where(t => !currentIds.Contains(t.Id)))
                    {
                        plan.Items.Add(Membership(true, target, team));
                    }

                    if (options.RemoveExtras)
                    {
                        foreach (var team in current.Where(t => !wantedIds.Contains(t.Id)))
                        {
                            plan.Items.Add(Membership(false, target, team));
                        }
                    }
                }
            }

            return plan;
        }

        /// <summary>The copy of a role that lives in <paramref name="businessUnitId"/>, or null when that business unit has none.</summary>
        private static RoleInfo CopyIn(SecurityGraph graph, RoleChoice choice, Guid? businessUnitId)
        {
            return graph.Roles.FirstOrDefault(r => r.RootKey == choice.RootKey && r.BusinessUnitId == businessUnitId);
        }

        /// <summary>One role added to / removed from a user or team, with the opposite change as its undo.</summary>
        private static ChangeItem RoleLink(string action, bool add, string entity, Guid id, string subject, string relationship, RoleInfo role)
        {
            var label = string.IsNullOrEmpty(role.BusinessUnitName) ? role.Name : $"{role.Name} ({role.BusinessUnitName})";
            var request = add
                ? ChangeExecutor.Associate(entity, id, relationship, "role", role.Id)
                : ChangeExecutor.Disassociate(entity, id, relationship, "role", role.Id);
            var undoRequest = add
                ? ChangeExecutor.Disassociate(entity, id, relationship, "role", role.Id)
                : ChangeExecutor.Associate(entity, id, relationship, "role", role.Id);
            var undoAction = add ? "Remove role" : "Add role";
            return Paired(new ChangeItem(action, subject, label, request), new ChangeItem(undoAction, subject, label, undoRequest));
        }

        private static ChangeItem Membership(bool add, UserInfo user, TeamInfo team)
        {
            // The team is the principal of the many-to-many link; the user is the related record.
            var request = add
                ? ChangeExecutor.Associate("team", team.Id, TeamMembershipRelationship, "systemuser", user.Id)
                : ChangeExecutor.Disassociate("team", team.Id, TeamMembershipRelationship, "systemuser", user.Id);
            var undoRequest = add
                ? ChangeExecutor.Disassociate("team", team.Id, TeamMembershipRelationship, "systemuser", user.Id)
                : ChangeExecutor.Associate("team", team.Id, TeamMembershipRelationship, "systemuser", user.Id);
            return Paired(
                new ChangeItem(add ? "Add to team" : "Remove from team", user.FullName, team.Name, request),
                new ChangeItem(add ? "Remove from team" : "Add to team", user.FullName, team.Name, undoRequest));
        }

        /// <summary>Links a change and its opposite to each other, so an undo can itself be undone.</summary>
        private static ChangeItem Paired(ChangeItem change, ChangeItem opposite)
        {
            change.Inverse = opposite;
            opposite.Inverse = change;
            return change;
        }

        private static void Summarize(ChangePlan plan, bool add, int already, int missing, string subjectKind)
        {
            if (add && already > 0)
            {
                plan.Notes.Add($"{already} assignment(s) skipped because the {subjectKind} already has it.");
            }
            else if (!add && missing > 0)
            {
                plan.Notes.Add($"{missing} removal(s) skipped because the {subjectKind} does not have it.");
            }
        }
    }
}
