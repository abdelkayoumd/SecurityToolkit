using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using SecurityToolkit.Models;
using SecurityToolkit.Services;
using Xunit;

namespace SecurityToolkit.Tests
{
    /// <summary>
    /// Business units Root and Sales. Seller and Admin exist in both; Auditor only in Root.
    /// Ada (Root) has Admin. Bob (Sales) has Seller and is in team Reviewers; so is Cy (Sales). Dee (Root) has nothing.
    /// Besides Reviewers there is a default team, an access team and a group team, which cannot be managed by hand.
    /// </summary>
    internal sealed class WriteOrg
    {
        public Guid Root = Guid.NewGuid(), Sales = Guid.NewGuid();
        public UserInfo Ada, Bob, Cy, Dee;
        public RoleInfo AdminRoot, AdminSales, AuditorRoot, SellerRoot, SellerSales;
        public TeamInfo Reviewers, DefaultTeam, AccessTeam, GroupTeam;
        public SecurityGraph Graph;

        public WriteOrg()
        {
            UserInfo User(string n, Guid bu, string buName) => new UserInfo { Id = Guid.NewGuid(), FullName = n, BusinessUnitId = bu, BusinessUnitName = buName };
            Ada = User("Ada", Root, "Root"); Bob = User("Bob", Sales, "Sales"); Cy = User("Cy", Sales, "Sales"); Dee = User("Dee", Root, "Root");

            RoleInfo Role(string n, Guid bu, string buName, Guid? root = null) => new RoleInfo { Id = Guid.NewGuid(), Name = n, BusinessUnitId = bu, BusinessUnitName = buName, RootRoleId = root };
            AdminRoot = Role("Admin", Root, "Root"); AdminSales = Role("Admin", Sales, "Sales", AdminRoot.Id);
            AuditorRoot = Role("Auditor", Root, "Root");
            SellerRoot = Role("Seller", Root, "Root"); SellerSales = Role("Seller", Sales, "Sales", SellerRoot.Id);

            TeamInfo Team(string n, int type, bool isDefault = false) => new TeamInfo { Id = Guid.NewGuid(), Name = n, TeamType = type, IsDefault = isDefault, BusinessUnitId = Root, BusinessUnitName = "Root" };
            Reviewers = Team("Reviewers", 0); DefaultTeam = Team("Root", 0, true); AccessTeam = Team("Shared", 1); GroupTeam = Team("Entra group", 2);

            Graph = new SecurityGraph(
                new[] { Ada, Bob, Cy, Dee },
                new[] { AdminRoot, AdminSales, AuditorRoot, SellerRoot, SellerSales },
                new[] { Reviewers, DefaultTeam, AccessTeam, GroupTeam },
                new[] { new BusinessUnitInfo { Id = Root, Name = "Root" }, new BusinessUnitInfo { Id = Sales, Name = "Sales", ParentId = Root } },
                new[]
                {
                    new UserTeamLink { UserId = Bob.Id, TeamId = Reviewers.Id },
                    new UserTeamLink { UserId = Cy.Id, TeamId = Reviewers.Id },
                    new UserTeamLink { UserId = Bob.Id, TeamId = DefaultTeam.Id }
                },
                new[] { new UserRoleLink { UserId = Ada.Id, RoleId = AdminRoot.Id }, new UserRoleLink { UserId = Bob.Id, RoleId = SellerSales.Id } },
                new TeamRoleLink[0]);
        }

        public RoleChoice Choice(string name) => Graph.RoleChoices().Single(c => c.Name == name);

        public static AssociateRequest Associate(ChangeItem item) => (AssociateRequest)item.Request;
        public static DisassociateRequest Disassociate(ChangeItem item) => (DisassociateRequest)item.Request;
    }

    public class UserRolePlanningTests
    {
        private readonly WriteOrg _o = new WriteOrg();

        [Fact]
        public void AddingARole_UsesTheCopyThatLivesInTheUsersBusinessUnit()
        {
            var inRoot = ChangePlanner.PlanUserRoles(_o.Graph, new[] { _o.Dee }, new[] { _o.Choice("Seller") }, add: true);
            var inSales = ChangePlanner.PlanUserRoles(_o.Graph, new[] { _o.Cy }, new[] { _o.Choice("Seller") }, add: true);

            Assert.Equal(_o.SellerRoot.Id, WriteOrg.Associate(Assert.Single(inRoot.Items)).RelatedEntities[0].Id);
            Assert.Equal(_o.SellerSales.Id, WriteOrg.Associate(Assert.Single(inSales.Items)).RelatedEntities[0].Id);
        }

        [Fact]
        public void AddingARole_SendsTheAssociationTheUserRoleLinkNeeds()
        {
            var item = Assert.Single(ChangePlanner.PlanUserRoles(_o.Graph, new[] { _o.Dee }, new[] { _o.Choice("Admin") }, true).Items);
            var request = WriteOrg.Associate(item);

            Assert.Equal("systemuser", request.Target.LogicalName);
            Assert.Equal(_o.Dee.Id, request.Target.Id);
            Assert.Equal("systemuserroles_association", request.Relationship.SchemaName);
            Assert.Equal("role", request.RelatedEntities[0].LogicalName);
            Assert.Equal("Add role", item.Action);
            Assert.Equal("Admin (Root)", item.Target);
        }

        [Fact]
        public void AddingARole_ThatTheUserAlreadyHas_PlansNothingAndSaysSo()
        {
            var plan = ChangePlanner.PlanUserRoles(_o.Graph, new[] { _o.Bob }, new[] { _o.Choice("Seller") }, add: true);

            Assert.Empty(plan.Items);
            Assert.Contains(plan.Notes, n => n.Contains("already has it"));
        }

        [Fact]
        public void AddingARole_WithNoCopyInTheUsersBusinessUnit_IsSkippedWithAReason()
        {
            var plan = ChangePlanner.PlanUserRoles(_o.Graph, new[] { _o.Cy }, new[] { _o.Choice("Auditor") }, add: true);

            Assert.Empty(plan.Items);
            var note = Assert.Single(plan.Notes);
            Assert.Contains("Cy", note);
            Assert.Contains("Auditor", note);
            Assert.Contains("Sales", note);
        }

        [Fact]
        public void RemovingARole_RemovesTheCopyTheUserHolds()
        {
            var item = Assert.Single(ChangePlanner.PlanUserRoles(_o.Graph, new[] { _o.Bob }, new[] { _o.Choice("Seller") }, add: false).Items);

            Assert.Equal("Remove role", item.Action);
            Assert.Equal(_o.SellerSales.Id, WriteOrg.Disassociate(item).RelatedEntities[0].Id);
        }

        [Fact]
        public void RemovingARole_TheUserDoesNotHave_PlansNothingAndSaysSo()
        {
            var plan = ChangePlanner.PlanUserRoles(_o.Graph, new[] { _o.Ada }, new[] { _o.Choice("Seller") }, add: false);

            Assert.Empty(plan.Items);
            Assert.Contains(plan.Notes, n => n.Contains("does not have it"));
        }

        [Fact]
        public void ManyUsersAndRoles_PlanOneChangePerPair_InNameOrder()
        {
            var plan = ChangePlanner.PlanUserRoles(_o.Graph, new[] { _o.Dee, _o.Ada }, new[] { _o.Choice("Seller"), _o.Choice("Auditor") }, add: true);

            Assert.Equal(new[] { "Ada→Auditor (Root)", "Ada→Seller (Root)", "Dee→Auditor (Root)", "Dee→Seller (Root)" },
                plan.Items.Select(i => i.Subject + "→" + i.Target));
        }

        [Fact]
        public void EveryChange_KnowsItsUndo()
        {
            var add = Assert.Single(ChangePlanner.PlanUserRoles(_o.Graph, new[] { _o.Dee }, new[] { _o.Choice("Admin") }, true).Items);
            var remove = Assert.Single(ChangePlanner.PlanUserRoles(_o.Graph, new[] { _o.Bob }, new[] { _o.Choice("Seller") }, false).Items);

            Assert.IsType<DisassociateRequest>(add.Inverse.Request);
            Assert.Equal("Remove role", add.Inverse.Action);
            Assert.IsType<AssociateRequest>(remove.Inverse.Request);
            Assert.Equal(_o.SellerSales.Id, WriteOrg.Associate(remove.Inverse).RelatedEntities[0].Id);
        }
    }

    public class TeamRolePlanningTests
    {
        private readonly WriteOrg _o = new WriteOrg();

        [Fact]
        public void AddingARole_UsesTheCopyInTheTeamsBusinessUnit()
        {
            var item = Assert.Single(ChangePlanner.PlanTeamRoles(_o.Graph, new[] { _o.Reviewers }, new[] { _o.Choice("Admin") }, true).Items);
            var request = WriteOrg.Associate(item);

            Assert.Equal("team", request.Target.LogicalName);
            Assert.Equal("teamroles_association", request.Relationship.SchemaName);
            Assert.Equal(_o.AdminRoot.Id, request.RelatedEntities[0].Id);
        }

        [Fact]
        public void AccessTeams_CannotHaveRoles()
        {
            var plan = ChangePlanner.PlanTeamRoles(_o.Graph, new[] { _o.AccessTeam }, new[] { _o.Choice("Admin") }, true);

            Assert.Empty(plan.Items);
            Assert.Contains(plan.Notes, n => n.Contains("access teams cannot have security roles"));
        }
    }

    public class MembershipPlanningTests
    {
        private readonly WriteOrg _o = new WriteOrg();

        [Fact]
        public void AddingAUserToATeam_AssociatesTheUserWithTheTeam()
        {
            var item = Assert.Single(ChangePlanner.PlanMembership(_o.Graph, new[] { _o.Ada }, new[] { _o.Reviewers }, true).Items);
            var request = WriteOrg.Associate(item);

            Assert.Equal("team", request.Target.LogicalName);
            Assert.Equal(_o.Reviewers.Id, request.Target.Id);
            Assert.Equal("teammembership_association", request.Relationship.SchemaName);
            Assert.Equal("systemuser", request.RelatedEntities[0].LogicalName);
            Assert.Equal(_o.Ada.Id, request.RelatedEntities[0].Id);
            Assert.Equal("Add to team", item.Action);
        }

        [Fact]
        public void AddingAnExistingMember_PlansNothing()
        {
            var plan = ChangePlanner.PlanMembership(_o.Graph, new[] { _o.Bob }, new[] { _o.Reviewers }, true);

            Assert.Empty(plan.Items);
            Assert.Contains(plan.Notes, n => n.Contains("already has it"));
        }

        [Fact]
        public void RemovingAMember_DisassociatesThem_AndNonMembersAreSkipped()
        {
            var plan = ChangePlanner.PlanMembership(_o.Graph, new[] { _o.Bob, _o.Ada }, new[] { _o.Reviewers }, false);

            var item = Assert.Single(plan.Items);
            Assert.Equal("Remove from team", item.Action);
            Assert.Equal(_o.Bob.Id, WriteOrg.Disassociate(item).RelatedEntities[0].Id);
            Assert.Contains(plan.Notes, n => n.Contains("does not have it"));
        }

        [Fact]
        public void DefaultAccessAndGroupTeams_AreNeverTouched_AndEachSaysWhy()
        {
            var plan = ChangePlanner.PlanMembership(_o.Graph, new[] { _o.Ada },
                new[] { _o.DefaultTeam, _o.AccessTeam, _o.GroupTeam }, true);

            Assert.Empty(plan.Items);
            Assert.Equal(3, plan.Notes.Count);
            Assert.Contains(plan.Notes, n => n.Contains("default team"));
            Assert.Contains(plan.Notes, n => n.Contains("Shared") && n.Contains("not managed by hand"));
            Assert.Contains(plan.Notes, n => n.Contains("Entra group") && n.Contains("not managed by hand"));
        }
    }

    public class ClonePlanningTests
    {
        private readonly WriteOrg _o = new WriteOrg();

        private ChangePlan Clone(UserInfo target, bool bu = false, bool roles = true, bool teams = true, bool extras = false)
        {
            return ChangePlanner.PlanClone(_o.Graph, _o.Bob, new[] { target }, new CloneOptions { BusinessUnit = bu, Roles = roles, Teams = teams, RemoveExtras = extras });
        }

        [Fact]
        public void Roles_AreCopiedAsTheTargetsBusinessUnitCopy()
        {
            var plan = Clone(_o.Dee, teams: false); // Dee is in Root: Seller (Root)

            var item = Assert.Single(plan.Items);
            Assert.Equal(_o.SellerRoot.Id, WriteOrg.Associate(item).RelatedEntities[0].Id);
        }

        [Fact]
        public void Teams_AreCopied_ButNotTheDefaultTeam()
        {
            var plan = Clone(_o.Dee, roles: false);

            var item = Assert.Single(plan.Items);
            Assert.Equal("Add to team", item.Action);
            Assert.Equal("Reviewers", item.Target);
        }

        [Fact]
        public void WhatTheTargetAlreadyHas_IsNotPlannedAgain()
        {
            var plan = Clone(_o.Cy); // Cy is already in Reviewers; only Seller is missing

            var item = Assert.Single(plan.Items);
            Assert.Equal("Add role", item.Action);
            Assert.Equal(_o.SellerSales.Id, WriteOrg.Associate(item).RelatedEntities[0].Id);
        }

        [Fact]
        public void RemoveExtras_MakesTheTargetMatchTheSource()
        {
            // Ada has Admin (Bob does not) and is in no team; Bob has Seller and Reviewers.
            var plan = Clone(_o.Ada, extras: true);

            Assert.Equal(new[] { "Add role:Seller (Root)", "Remove role:Admin (Root)", "Add to team:Reviewers" },
                plan.Items.Select(i => i.Action + ":" + i.Target));
        }

        [Fact]
        public void WithoutRemoveExtras_NothingIsRemoved()
        {
            var plan = Clone(_o.Ada, extras: false);

            Assert.DoesNotContain(plan.Items, i => i.Action.StartsWith("Remove"));
        }

        [Fact]
        public void ChangingBusinessUnit_ComesFirst_ClearsTheTargetsRoles_AndCannotBeUndone()
        {
            var plan = Clone(_o.Ada, bu: true, extras: true); // Ada moves from Root to Sales

            Assert.Equal("Change business unit", plan.Items[0].Action);
            var move = (SetBusinessSystemUserRequest)plan.Items[0].Request;
            Assert.Equal(_o.Ada.Id, move.UserId);
            Assert.Equal(_o.Sales, move.BusinessId);
            Assert.Null(plan.Items[0].Inverse);
            Assert.False(plan.IsReversible);

            // Roles are then taken from the source in Sales, and Ada's old Admin is not "removed" - the move did that.
            Assert.Contains(plan.Items, i => i.Action == "Add role" && WriteOrg.Associate(i).RelatedEntities[0].Id == _o.SellerSales.Id);
            Assert.DoesNotContain(plan.Items, i => i.Action == "Remove role");
            Assert.Contains(plan.Notes, n => n.Contains("removes all their current security roles"));
        }

        [Fact]
        public void BusinessUnit_IsNotChanged_WhenAlreadyTheSame()
        {
            var plan = Clone(_o.Cy, bu: true); // Cy is already in Sales

            Assert.DoesNotContain(plan.Items, i => i.Action == "Change business unit");
        }

        [Fact]
        public void TheSourceIsNeverItsOwnTarget()
        {
            var plan = ChangePlanner.PlanClone(_o.Graph, _o.Bob, new[] { _o.Bob }, new CloneOptions { Roles = true, Teams = true });

            Assert.Empty(plan.Items);
        }

        [Fact]
        public void NothingIsPlanned_ForWhatWasNotAsked()
        {
            Assert.Empty(ChangePlanner.PlanClone(_o.Graph, _o.Bob, new[] { _o.Dee }, new CloneOptions()).Items);
        }
    }

    public class UndoPlanningTests
    {
        private readonly WriteOrg _o = new WriteOrg();

        [Fact]
        public void Undo_RunsTheInversesLastChangeFirst()
        {
            var plan = ChangePlanner.PlanClone(_o.Graph, _o.Bob, new[] { _o.Ada }, new CloneOptions { Roles = true, Teams = true, RemoveExtras = true });
            Assert.True(plan.IsReversible);

            var undo = ChangePlan.UndoOf(plan.Items);

            Assert.Equal(plan.Items.Count, undo.Items.Count);
            Assert.Equal(plan.Items.Select(i => i.Inverse.Action).Reverse(), undo.Items.Select(i => i.Action));
        }

        [Fact]
        public void Undo_OfAnUndo_RestoresTheOriginalChanges()
        {
            var plan = ChangePlanner.PlanUserRoles(_o.Graph, new[] { _o.Dee }, new[] { _o.Choice("Admin") }, true);

            var again = ChangePlan.UndoOf(ChangePlan.UndoOf(plan.Items).Items);

            Assert.Equal("Add role", Assert.Single(again.Items).Action);
        }

        [Fact]
        public void Undo_ReportsChangesItCannotReverse()
        {
            var plan = ChangePlanner.PlanClone(_o.Graph, _o.Bob, new[] { _o.Ada }, new CloneOptions { BusinessUnit = true, Roles = true });

            var undo = ChangePlan.UndoOf(plan.Items);

            Assert.Contains(undo.Notes, n => n.StartsWith("Cannot be undone: Change business unit"));
            Assert.DoesNotContain(undo.Items, i => i.Action == "Change business unit");
        }
    }
}
