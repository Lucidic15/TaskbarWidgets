using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace RDPLauncher
{
    /// <summary>Asks for the name or address of a computer to connect to.</summary>
    internal sealed class AddComputerDialog : Form
    {
        readonly TextBox computerBox = new TextBox();

        public AddComputerDialog(IEnumerable<string> suggestions)
        {
            Text = "Add a computer";
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
                Text = "Computer name or IP address, as you'd type it in Remote Desktop Connection\n(for example workpc, workpc.contoso.com or 192.168.1.20:3390):",
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 6),
            });

            computerBox.Width = LogicalToDeviceUnits(380);
            computerBox.Margin = new Padding(0, 0, 0, 12);
            // Suggest the computers Remote Desktop Connection has been used with.
            var source = new AutoCompleteStringCollection();
            foreach (string suggestion in suggestions)
                source.Add(suggestion);
            computerBox.AutoCompleteCustomSource = source;
            computerBox.AutoCompleteSource = AutoCompleteSource.CustomSource;
            computerBox.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
            layout.Controls.Add(computerBox);

            var addButton = new Button { Text = "Add", DialogResult = DialogResult.OK, Enabled = false, AutoSize = true };
            var cancelButton = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
            computerBox.TextChanged += (sender, e) => addButton.Enabled = Computer.Length > 0;
            var buttons = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true,
                Dock = DockStyle.Fill,
                Margin = Padding.Empty,
            };
            buttons.Controls.Add(cancelButton);
            buttons.Controls.Add(addButton);
            layout.Controls.Add(buttons);

            Controls.Add(layout);
            AcceptButton = addButton;
            CancelButton = cancelButton;
        }

        /// <summary>What was typed, without spaces around it.</summary>
        public string Computer => computerBox.Text.Trim();

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            // Opened from the taskbar or tray, which don't hand over the keyboard by themselves.
            Activate();
            computerBox.Focus();
        }
    }
}
