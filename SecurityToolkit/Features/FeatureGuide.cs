using System.Collections.Generic;
using System.Linq;

namespace SecurityToolkit.Features
{
    /// <summary>The heading a guide entry is listed under on the Home page.</summary>
    internal enum GuideSection
    {
        LookUp,
        UnderstandAccess,
        Compare,
        Review,
        Change
    }

    /// <summary>What to tell people about one screen: the question it answers, what it does, and how to use it.</summary>
    internal sealed class GuideEntry
    {
        public string FeatureId { get; set; }
        public GuideSection Section { get; set; }

        /// <summary>The question an administrator would ask, which this screen answers.</summary>
        public string Question { get; set; }

        /// <summary>One line: what the screen does.</summary>
        public string Summary { get; set; }

        /// <summary>The steps to get an answer, in order.</summary>
        public string[] Steps { get; set; }
    }

    /// <summary>
    /// All the explanatory text in one place, keyed by feature id: the Home page cards and each screen's "How it works".
    /// A feature without an entry simply has no card and no help; removing a feature leaves nothing dangling.
    /// </summary>
    internal static class FeatureGuide
    {
        public const string ChangeNote = "Nothing changes until you approve a review that lists every change. You can save a restore file and undo afterwards.";

        public static IReadOnlyList<GuideEntry> All { get; } = new[]
        {
            new GuideEntry
            {
                FeatureId = "users-and-teams", Section = GuideSection.LookUp,
                Question = "What teams and roles does this person have?",
                Summary = "Lists users; for the ones you select, shows their teams and every role, direct or inherited from a team.",
                Steps = new[] { "Click Load users.", "Select one or more users (Ctrl or Shift for several).", "Their direct roles and teams appear on the right. A team's roles are the ones the user inherits from it." }
            },
            new GuideEntry
            {
                FeatureId = "users-by-role", Section = GuideSection.LookUp,
                Question = "Who holds this security role?",
                Summary = "Pick roles and see every user who has them, directly or through a team.",
                Steps = new[] { "Select one or more roles on the left.", "The users who hold them appear on the right, with how they hold each role.", "Export to Excel to keep the list." }
            },
            new GuideEntry
            {
                FeatureId = "team-members", Section = GuideSection.LookUp,
                Question = "Who is in this team?",
                Summary = "Pick teams and see their members.",
                Steps = new[] { "Select one or more teams on the left.", "Their members appear on the right.", "Export to Excel to keep the list." }
            },
            new GuideEntry
            {
                FeatureId = "business-unit-tree", Section = GuideSection.LookUp,
                Question = "Who is in this business unit?",
                Summary = "The business units as a tree. Select one to see its users, teams or the roles in use there.",
                Steps = new[] { "Read the tree: each unit shows how many users and teams it holds.", "Select a unit, then choose what to list: Users (with their teams and roles), Teams, or Roles in use.", "Tick Include child business units to take the units below it as well. Export the list, or the users in detail." }
            },
            new GuideEntry
            {
                FeatureId = "user-access-matrix", Section = GuideSection.UnderstandAccess,
                Question = "What can this user do?",
                Summary = "Every table against the eight privileges, showing how far the user's roles reach.",
                Steps = new[] { "Select a user on the left.", "Each cell shows the widest reach among all their roles: own records, business unit, parent and child units, or organization.", "A blank cell means that privilege does not exist for the table." }
            },
            new GuideEntry
            {
                FeatureId = "explain-access", Section = GuideSection.UnderstandAccess,
                Question = "Why can this user delete accounts?",
                Summary = "Pick a privilege and a table, and see every route by which the selected users get it.",
                Steps = new[] { "Select users on the left.", "Choose a privilege and a table in the sentence above the lists.", "Each row is one route: the role, whether it is direct or through which team, and how far it reaches. The strongest route is ticked." }
            },
            new GuideEntry
            {
                FeatureId = "privilege-finder", Section = GuideSection.UnderstandAccess,
                Question = "Which roles let people delete on a table?",
                Summary = "Pick a privilege, a depth and tables, and see which roles grant it.",
                Steps = new[] { "Choose the privilege and the minimum depth in the sentence above the lists.", "Select one or more tables on the left.", "The roles that grant it, in each business unit, appear on the right." }
            },
            new GuideEntry
            {
                FeatureId = "compare-users", Section = GuideSection.Compare,
                Question = "How are these two users different?",
                Summary = "Two users side by side: business unit, roles and teams, with what only one of them has.",
                Steps = new[] { "Ctrl-click two users on the left.", "The right lists what differs. Untick Differences only to see what they share as well.", "Useful before cloning a user, or to find out why one user can do more." }
            },
            new GuideEntry
            {
                FeatureId = "compare-roles", Section = GuideSection.Compare,
                Question = "How are these two roles different?",
                Summary = "Two roles privilege by privilege: what only one grants and where one reaches further.",
                Steps = new[] { "Ctrl-click two roles on the left.", "The right lists, table by table, where they differ, with the depth each grants.", "Untick Differences only to see the privileges they share." }
            },
            new GuideEntry
            {
                FeatureId = "privileged-users", Section = GuideSection.Review,
                Question = "Is anyone over-privileged?",
                Summary = "Finds the roles that can delete across the organization and who holds them.",
                Steps = new[] { "Read the six checks on the left with their counts.", "Select a check to see only its findings.", "Disabled users and service accounts that still hold these roles are checked separately." }
            },
            new GuideEntry
            {
                FeatureId = "cleanup-report", Section = GuideSection.Review,
                Question = "What can I tidy up?",
                Summary = "Finds users, roles and teams that look forgotten, and lets you clean up the disabled users.",
                Steps = new[] { "Read the checks on the left with their counts.", "Select a check to see only its findings.", "For disabled users, use the buttons to take them out of teams or remove their roles. " + ChangeNote }
            },
            new GuideEntry
            {
                FeatureId = "assign-user-roles", Section = GuideSection.Change,
                Question = "Give or remove security roles for users",
                Summary = "Add roles to users, or remove them, in one go.",
                Steps = new[] { "Select users on the left and the roles on the right.", "Click Add roles or Remove roles.", ChangeNote }
            },
            new GuideEntry
            {
                FeatureId = "assign-team-roles", Section = GuideSection.Change,
                Question = "Give or remove security roles for teams",
                Summary = "Add roles to teams, or remove them, in one go.",
                Steps = new[] { "Select teams on the left and the roles on the right.", "Click Add roles or Remove roles.", ChangeNote }
            },
            new GuideEntry
            {
                FeatureId = "assign-team-membership", Section = GuideSection.Change,
                Question = "Add or remove people in teams",
                Summary = "Put users into teams, or take them out.",
                Steps = new[] { "Select users on the left and teams on the right.", "Click Add to teams or Remove from teams.", ChangeNote }
            },
            new GuideEntry
            {
                FeatureId = "clone-user", Section = GuideSection.Change,
                Question = "Set up a new user like an existing one",
                Summary = "Copy one user's business unit, roles and teams onto other users.",
                Steps = new[] { "Select the user to copy from on the left and the users to copy to on the right.", "Tick what to copy, then click Clone profile.", ChangeNote }
            }
        };

        public static GuideEntry Find(string featureId)
        {
            return string.IsNullOrEmpty(featureId) ? null : All.FirstOrDefault(e => e.FeatureId == featureId);
        }

        public static string Heading(GuideSection section)
        {
            switch (section)
            {
                case GuideSection.LookUp: return "Look up";
                case GuideSection.UnderstandAccess: return "Understand access";
                case GuideSection.Compare: return "Compare";
                case GuideSection.Review: return "Review";
                default: return "Change (every change is reviewed first)";
            }
        }
    }
}
