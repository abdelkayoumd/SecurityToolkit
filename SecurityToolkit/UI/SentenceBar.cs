using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Label = System.Windows.Forms.Label;

namespace SecurityToolkit.UI
{
    /// <summary>How the answer at the end of a sentence is coloured.</summary>
    internal enum SentenceTone
    {
        Neutral,
        Yes,
        No
    }

    /// <summary>
    /// The line above a screen's results that asks its question with the choices inside it, and states the answer at
    /// its end: "Why can [Ada] [Read] on [Account]?  Yes, at Organization level". Every sentence screen uses it, so
    /// they all look and read the same.
    /// </summary>
    internal sealed class SentenceBar : FlowLayoutPanel
    {
        private readonly Label _answer;

        public SentenceBar()
        {
            Name = "Sentence";
            Dock = DockStyle.Top;
            Height = 36;
            WrapContents = false;
            AutoScroll = false;
            Padding = new Padding(10, 0, 0, 0);
            BackColor = Theme.PageBackground;
            _answer = new Label
            {
                Name = "SentenceAnswer",
                AutoSize = true,
                Font = Theme.FontBold,
                Margin = new Padding(10, 10, 0, 0),
                Visible = false
            };
            Controls.Add(_answer);
        }

        /// <summary>A piece of the sentence in words. Its text can be changed later (a count, a name).</summary>
        public Label Words(string text = "")
        {
            var label = new Label
            {
                Text = text,
                AutoSize = true,
                Font = Theme.FontBold,
                ForeColor = Theme.TextSecondary,
                Margin = new Padding(0, 10, 5, 0)
            };
            Append(label);
            return label;
        }

        /// <summary>A choice inside the sentence (a drop-down).</summary>
        public T Choice<T>(T control) where T : Control
        {
            control.Margin = new Padding(0, 7, 6, 0);
            control.Font = Theme.FontRegular;
            Append(control);
            return control;
        }

        /// <summary>The answer at the end of the sentence, coloured by <paramref name="tone"/>; null or empty hides it.</summary>
        public void Answer(string text, SentenceTone tone = SentenceTone.Neutral)
        {
            _answer.Text = text ?? string.Empty;
            _answer.ForeColor = tone == SentenceTone.Yes ? Theme.Success : tone == SentenceTone.No ? Theme.Danger : Theme.TextPrimary;
            _answer.Visible = !string.IsNullOrEmpty(text);
        }

        public string AnswerText => _answer.Visible ? _answer.Text : string.Empty;

        /// <summary>The whole sentence as plain text, with each choice as it reads now: what an export says it shows.</summary>
        public string PlainText()
        {
            var parts = Controls.Cast<Control>().Where(c => c.Visible).Select(c => c.Text?.Trim()).Where(t => !string.IsNullOrEmpty(t));
            return string.Join(" ", parts).Replace(" ?", "?");
        }

        private void Append(Control control)
        {
            Controls.Add(control);
            Controls.SetChildIndex(_answer, Controls.Count - 1); // the answer stays at the end
        }
    }

    /// <summary>
    /// A drop-down you can type into: typing narrows the list, and Enter picks the item whose name was typed. For long
    /// lists inside a sentence (tables, users, roles).
    /// </summary>
    internal sealed class PickBox : ComboBox
    {
        private sealed class Item
        {
            public Item(object value, string text) { Value = value; Text = text; }
            public object Value { get; }
            public string Text { get; }
            public override string ToString() => Text;
        }

        private bool _silent;

        public PickBox()
        {
            DropDownStyle = ComboBoxStyle.DropDown;
            AutoCompleteMode = AutoCompleteMode.SuggestAppend;
            AutoCompleteSource = AutoCompleteSource.ListItems;
        }

        /// <summary>Raised when the user picks an item (not when the screen sets one with <see cref="Show"/>).</summary>
        public event EventHandler Picked;

        /// <summary>Fills the list, keeping the current item when it is still there.</summary>
        public void SetItems<T>(IEnumerable<T> items, Func<T, string> text)
        {
            var current = Value;
            _silent = true;
            try
            {
                Items.Clear();
                Items.AddRange(items.Select(i => (object)new Item(i, text(i))).ToArray());
                if (current != null)
                {
                    SelectIndex(i => Equals(((Item)Items[i]).Value, current));
                }
            }
            finally
            {
                _silent = false;
            }
        }

        public object Value => (SelectedItem as Item)?.Value;

        public T Selected<T>() where T : class => Value as T;

        /// <summary>Shows the first item that matches, without raising <see cref="Picked"/>; false when none does.</summary>
        public bool Show(Func<object, bool> match)
        {
            _silent = true;
            try
            {
                return SelectIndex(i => match(((Item)Items[i]).Value));
            }
            finally
            {
                _silent = false;
            }
        }

        public void Clear()
        {
            _silent = true;
            Items.Clear();
            Text = string.Empty;
            _silent = false;
        }

        private bool SelectIndex(Func<int, bool> match)
        {
            for (var i = 0; i < Items.Count; i++)
            {
                if (match(i))
                {
                    SelectedIndex = i;
                    return true;
                }
            }

            return false;
        }

        protected override void OnSelectedIndexChanged(EventArgs e)
        {
            base.OnSelectedIndexChanged(e);
            if (!_silent && SelectedIndex >= 0)
            {
                Picked?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>Enter picks the item whose name (or the text in brackets) was typed, when exactly one has it.</summary>
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                PickTyped();
                e.SuppressKeyPress = true;
            }

            base.OnKeyDown(e);
        }

        private void PickTyped()
        {
            var typed = Text.Trim();
            if (typed.Length == 0)
            {
                return;
            }

            var matches = Enumerable.Range(0, Items.Count).Where(i => Names(((Item)Items[i]).Text).Any(n => string.Equals(n, typed, StringComparison.CurrentCultureIgnoreCase))).ToList();
            if (matches.Count == 1)
            {
                SelectedIndex = matches[0];
            }
        }

        /// <summary>"Account (account)" answers to "Account (account)", "Account" and "account".</summary>
        private static IEnumerable<string> Names(string text)
        {
            yield return text;
            var open = text.LastIndexOf(" (", StringComparison.Ordinal);
            if (open > 0 && text.EndsWith(")", StringComparison.Ordinal))
            {
                yield return text.Substring(0, open);
                yield return text.Substring(open + 2, text.Length - open - 3);
            }
        }
    }
}
