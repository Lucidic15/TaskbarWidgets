using System;
using System.Drawing;
using System.Windows.Forms;

namespace AudioFlipper
{
    /// <summary>A small box for typing a new name, for example for a webcam.</summary>
    internal sealed class RenameDialog : Form
    {
        readonly TextBox nameBox;

        public RenameDialog(string title, string prompt, string currentName)
        {
            Text = title;
            Font = SystemFonts.MessageBoxFont;
            AutoScaleDimensions = new SizeF(96f, 96f);
            AutoScaleMode = AutoScaleMode.Dpi;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowIcon = false;
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;
            ClientSize = new Size(380, 130);

            var label = new Label
            {
                Text = prompt,
                Location = new Point(12, 12),
                Size = new Size(356, 36),
            };
            nameBox = new TextBox
            {
                Text = currentName,
                Location = new Point(12, 54),
                Size = new Size(356, 23),
                MaxLength = 60,
            };
            var ok = new Button
            {
                Text = "OK",
                DialogResult = DialogResult.OK,
                Location = new Point(212, 92),
                Size = new Size(75, 26),
            };
            var cancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Location = new Point(293, 92),
                Size = new Size(75, 26),
            };
            Controls.AddRange(new Control[] { label, nameBox, ok, cancel });
            AcceptButton = ok;
            CancelButton = cancel;

            Shown += (sender, e) =>
            {
                Activate();
                nameBox.Focus();
                nameBox.SelectAll();
            };
        }

        /// <summary>The name typed in, trimmed; empty when the box was cleared.</summary>
        public string EnteredName => nameBox.Text.Trim();
    }
}
