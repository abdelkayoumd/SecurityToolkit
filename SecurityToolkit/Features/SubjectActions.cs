using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using SecurityToolkit.Services;

namespace SecurityToolkit.Features
{
    /// <summary>One "open this somewhere else" entry: for a kind of subject, a screen and what it answers.</summary>
    internal sealed class SubjectAction
    {
        public SubjectAction(SubjectKind kind, string featureId, string text)
        {
            Kind = kind;
            FeatureId = featureId;
            Text = text;
        }

        public SubjectKind Kind { get; }
        public string FeatureId { get; }

        /// <summary>The menu text: the question the screen answers about the subject.</summary>
        public string Text { get; }
    }

    /// <summary>
    /// Which screens can be opened on a user, team, role or table, from the right-click menu of any list.
    /// The single place to edit when a screen learns to take a subject.
    /// </summary>
    internal static class SubjectActions
    {
        public static readonly IReadOnlyList<SubjectAction> All = new[]
        {
            new SubjectAction(SubjectKind.User, "user-access-matrix", "What can this user do?"),
            new SubjectAction(SubjectKind.User, "explain-access", "Why does this user have access?"),
            new SubjectAction(SubjectKind.User, "record-access", "Check their access to a record"),
            new SubjectAction(SubjectKind.User, "compare-users", "Compare with another user"),
            new SubjectAction(SubjectKind.User, "clone-user", "Copy this user's access to others"),
            new SubjectAction(SubjectKind.User, "assign-user-roles", "Add or remove roles"),
            new SubjectAction(SubjectKind.User, "assign-team-membership", "Add to or remove from teams"),

            new SubjectAction(SubjectKind.Team, "team-members", "See the members"),
            new SubjectAction(SubjectKind.Team, "record-access", "Check its access to a record"),
            new SubjectAction(SubjectKind.Team, "assign-team-roles", "Add or remove roles"),

            new SubjectAction(SubjectKind.Role, "users-by-role", "See who holds this role"),
            new SubjectAction(SubjectKind.Role, "compare-roles", "Compare with another role"),

            new SubjectAction(SubjectKind.Table, "privilege-finder", "Which roles grant access to this table?")
        };

        /// <summary>The menu entries opening each screen that takes <paramref name="subject"/>, except <paramref name="currentFeatureId"/>.</summary>
        public static IEnumerable<ToolStripItem> MenuItems(Subject subject, string currentFeatureId, Action<string, Subject> open)
        {
            foreach (var action in For(subject.Kind, currentFeatureId))
            {
                var item = new ToolStripMenuItem(action.Text) { Tag = action.FeatureId };
                item.Click += (s, e) => open(action.FeatureId, subject);
                yield return item;
            }
        }

        /// <summary>"User: Bob", the heading of a subject's menu.</summary>
        public static string Heading(Subject subject)
        {
            return char.ToUpperInvariant(subject.KindText[0]) + subject.KindText.Substring(1) + ": " + subject.Name;
        }

        /// <summary>The actions for a kind of subject, leaving out the screen the menu was opened from.</summary>
        public static IReadOnlyList<SubjectAction> For(SubjectKind kind, string currentFeatureId)
        {
            return All.Where(a => a.Kind == kind && !string.Equals(a.FeatureId, currentFeatureId, StringComparison.OrdinalIgnoreCase)).ToList();
        }
    }
}
