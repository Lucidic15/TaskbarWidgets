using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace RDPLauncher
{
    /// <summary>Asks for one line of text: a computer to add, or a name for a connection.</summary>
    internal sealed class InputDialog : Form
    {
        readonly TextBox textBox = new TextBox();

        /// <param name="suggestions">Offered as the text is typed; may be null.</param>
        /// <param name="allowSpaces">False for computer names, which have none.</param>
        /// <param name="allowEmpty">Whether OK may be clicked with nothing typed.</param>
        public InputDialog(string title, string prompt, string acceptText, string text,
            IEnumerable<string> suggestions, bool allowSpaces, bool allowEmpty)
        {
            Text = title;
            Font = SystemFonts.MessageBoxFont;
            AutoScaleMode = AutoScaleMode.Font;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(12);
            try
            {
                Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch (Exception)
            {
                ShowIcon = false;
            }

            var layout = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Dock = DockStyle.Fill };
            layout.Controls.Add(new Label
            {
                Text = prompt,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 6),
            });

            textBox.Width = LogicalToDeviceUnits(380);
            textBox.Margin = new Padding(0, 0, 0, 12);
            textBox.Text = text ?? "";
            if (suggestions != null)
            {
                var source = new AutoCompleteStringCollection();
                foreach (string suggestion in suggestions)
                    source.Add(suggestion);
                textBox.AutoCompleteCustomSource = source;
                textBox.AutoCompleteSource = AutoCompleteSource.CustomSource;
                textBox.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
            }
            layout.Controls.Add(textBox);

            var acceptButton = new Button { Text = acceptText, DialogResult = DialogResult.OK, AutoSize = true };
            var cancelButton = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
            void UpdateAcceptButton() =>
                acceptButton.Enabled = (allowEmpty || Value.Length > 0) && (allowSpaces || !Value.Any(char.IsWhiteSpace));
            textBox.TextChanged += (sender, e) => UpdateAcceptButton();
            UpdateAcceptButton();
            var buttons = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true,
                Dock = DockStyle.Fill,
                Margin = Padding.Empty,
            };
            buttons.Controls.Add(cancelButton);
            buttons.Controls.Add(acceptButton);
            layout.Controls.Add(buttons);

            Controls.Add(layout);
            AcceptButton = acceptButton;
            CancelButton = cancelButton;
        }

        /// <summary>What was typed, without spaces around it.</summary>
        public string Value => textBox.Text.Trim();

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            // Opened from the taskbar or tray, which don't hand over the keyboard by themselves.
            Activate();
            textBox.Focus();
            textBox.SelectAll();
        }
    }
}
