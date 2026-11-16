using System;
using System.Collections.Generic;
using System.Linq;

namespace SecurityToolkit.Services
{
    /// <summary>
    /// What the toolkit remembers between sessions, saved by XrmToolBox with the other tools' settings.
    /// Public with settable lists because it is written and read as XML.
    /// </summary>
    public sealed class ToolkitSettings
    {
        /// <summary>The screen shown last, for each environment.</summary>
        public List<EnvironmentState> Environments { get; set; } = new List<EnvironmentState>();

        /// <summary>Command-bar options left ticked, as "screen|option".</summary>
        public List<string> CheckedOptions { get; set; } = new List<string>();

        /// <summary>Command-bar options left unticked although ticked by default, as "screen|option".</summary>
        public List<string> UncheckedOptions { get; set; } = new List<string>();
    }

    public sealed class EnvironmentState
    {
        /// <summary>The environment's address (or name when there is no address).</summary>
        public string Key { get; set; }

        public string LastFeature { get; set; }

        /// <summary>Users, teams, roles and tables opened lately, most recent first.</summary>
        public List<RecentSubject> Recent { get; set; } = new List<RecentSubject>();
    }

    public sealed class RecentSubject
    {
        public string Kind { get; set; }
        public Guid Id { get; set; }
        public string Name { get; set; }
        public string LogicalName { get; set; }
    }

    /// <summary>Reads and changes <see cref="ToolkitSettings"/>; saving is left to the shell.</summary>
    internal sealed class ToolkitState
    {
        public const int MaxRecent = 8;
        private const int MaxEnvironments = 20;

        public ToolkitState(ToolkitSettings settings)
        {
            Settings = settings ?? new ToolkitSettings();
            Settings.Environments = Settings.Environments ?? new List<EnvironmentState>();
            Settings.CheckedOptions = Settings.CheckedOptions ?? new List<string>();
            Settings.UncheckedOptions = Settings.UncheckedOptions ?? new List<string>();
        }

        public ToolkitSettings Settings { get; }

        /// <summary>The screen to reopen for an environment, or null.</summary>
        public string LastFeature(string environment)
        {
            return Find(environment)?.LastFeature;
        }

        public void SetLastFeature(string environment, string featureId)
        {
            var state = FindOrAdd(environment);
            if (state != null)
            {
                state.LastFeature = featureId;
            }
        }

        public IReadOnlyList<Subject> Recent(string environment)
        {
            var state = Find(environment);
            if (state == null)
            {
                return new Subject[0];
            }

            return state.Recent
                .Where(r => Enum.TryParse(r.Kind, out SubjectKind _))
                .Select(r => new Subject { Kind = (SubjectKind)Enum.Parse(typeof(SubjectKind), r.Kind), Id = r.Id, Name = r.Name, LogicalName = r.LogicalName })
                .ToList();
        }

        /// <summary>Puts a subject first in the environment's recent list, once, keeping the list short.</summary>
        public void AddRecent(string environment, Subject subject)
        {
            var state = FindOrAdd(environment);
            if (state == null || subject == null)
            {
                return;
            }

            state.Recent.RemoveAll(r => r.Kind == subject.Kind.ToString() && (subject.Kind == SubjectKind.Table
                ? string.Equals(r.LogicalName ?? r.Name, subject.LogicalName ?? subject.Name, StringComparison.OrdinalIgnoreCase)
                : r.Id == subject.Id));
            state.Recent.Insert(0, new RecentSubject { Kind = subject.Kind.ToString(), Id = subject.Id, Name = subject.Name, LogicalName = subject.LogicalName });
            if (state.Recent.Count > MaxRecent)
            {
                state.Recent.RemoveRange(MaxRecent, state.Recent.Count - MaxRecent);
            }
        }

        /// <summary>
        /// Whether an option of a screen was left ticked, or null when it was never changed (keep its default).
        /// Write options of the screens that change security are never remembered: those start from their defaults.
        /// </summary>
        public bool? Option(string featureId, string option)
        {
            if (!Remembers(featureId, option))
            {
                return null;
            }

            var key = featureId + "|" + option;
            if (Settings.CheckedOptions.Contains(key))
            {
                return true;
            }

            return Settings.UncheckedOptions.Contains(key) ? false : (bool?)null;
        }

        public void SetOption(string featureId, string option, bool isChecked)
        {
            if (!Remembers(featureId, option))
            {
                return;
            }

            var key = featureId + "|" + option;
            Settings.CheckedOptions.Remove(key);
            Settings.UncheckedOptions.Remove(key);
            (isChecked ? Settings.CheckedOptions : Settings.UncheckedOptions).Add(key);
        }

        /// <summary>Viewing options are remembered; the options of "Clone a user" decide what gets changed, so they are not.</summary>
        internal static bool Remembers(string featureId, string option)
        {
            return featureId != "clone-user" || option == "Include disabled users";
        }

        private EnvironmentState Find(string environment)
        {
            return string.IsNullOrEmpty(environment)
                ? null
                : Settings.Environments.FirstOrDefault(e => string.Equals(e.Key, environment, StringComparison.OrdinalIgnoreCase));
        }

        private EnvironmentState FindOrAdd(string environment)
        {
            if (string.IsNullOrEmpty(environment))
            {
                return null;
            }

            var state = Find(environment);
            if (state != null)
            {
                // Most recently used first, so the oldest falls off the end.
                Settings.Environments.Remove(state);
                Settings.Environments.Insert(0, state);
                return state;
            }

            state = new EnvironmentState { Key = environment };
            Settings.Environments.Insert(0, state);
            if (Settings.Environments.Count > MaxEnvironments)
            {
                Settings.Environments.RemoveRange(MaxEnvironments, Settings.Environments.Count - MaxEnvironments);
            }

            return state;
        }
    }
}
