using System;
using System.Collections.Generic;
using System.Windows.Forms;
using FolderMoveProtector.Hook;

namespace FolderMoveProtector.Settings
{
    /// <summary>
    /// Everything is built in code rather than with the WinForms designer -
    /// simpler to read and hand over as a plain text file, and this form is
    /// simple enough that a designer surface doesn't add much. Feel free to
    /// open it in the Visual Studio designer anyway; it'll still work.
    /// </summary>
    public class SettingsForm : Form
    {
        private readonly ListBox _rootsListBox = new ListBox();
        private readonly TextBox _pathTextBox = new TextBox();

        public SettingsForm()
        {
            Text = "Folder Move Protector - Settings";
            ClientSize = new System.Drawing.Size(544, 386);
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            FormBorderStyle = FormBorderStyle.FixedDialog;

            var infoLabel = new Label
            {
                Left = 12,
                Top = 12,
                Width = 520,
                Height = 45,
                Text = "Moving a folder into or out of any folder listed below will prompt for " +
                       "confirmation. This applies immediately, machine-wide, for every account " +
                       "that logs into this computer."
            };

            var listLabel = new Label { Left = 12, Top = 60, Width = 300, Text = "Protected folders:" };

            _rootsListBox.Left = 12;
            _rootsListBox.Top = 80;
            _rootsListBox.Width = 520;
            _rootsListBox.Height = 190;

            var pathLabel = new Label { Left = 12, Top = 282, Width = 400, Text = "Add a folder (network path preferred):" };

            _pathTextBox.Left = 12;
            _pathTextBox.Top = 302;
            _pathTextBox.Width = 400;

            var browseButton = new Button { Text = "Browse...", Left = 420, Top = 300, Width = 112 };
            browseButton.Click += BrowseButton_Click;

            var addButton = new Button { Text = "Add", Left = 12, Top = 332, Width = 90 };
            addButton.Click += AddButton_Click;

            var removeButton = new Button { Text = "Remove Selected", Left = 110, Top = 332, Width = 130 };
            removeButton.Click += RemoveButton_Click;

            var saveButton = new Button { Text = "Save", Left = 350, Top = 332, Width = 90 };
            saveButton.Click += SaveButton_Click;

            var closeButton = new Button { Text = "Close", Left = 446, Top = 332, Width = 86 };
            closeButton.Click += (s, e) => Close();

            Controls.Add(infoLabel);
            Controls.Add(listLabel);
            Controls.Add(_rootsListBox);
            Controls.Add(pathLabel);
            Controls.Add(_pathTextBox);
            Controls.Add(browseButton);
            Controls.Add(addButton);
            Controls.Add(removeButton);
            Controls.Add(saveButton);
            Controls.Add(closeButton);

            Load += (s, e) => LoadCurrentRoots();
        }

        private void LoadCurrentRoots()
        {
            _rootsListBox.Items.Clear();
            foreach (string root in ProtectedRootsStore.GetProtectedRoots())
                _rootsListBox.Items.Add(root);
        }

        private void BrowseButton_Click(object sender, EventArgs e)
        {
            using (var dialog = new FolderBrowserDialog { Description = "Select the folder to protect" })
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                    _pathTextBox.Text = dialog.SelectedPath;
            }
        }

        private void AddButton_Click(object sender, EventArgs e)
        {
            string path = _pathTextBox.Text.Trim();
            if (string.IsNullOrEmpty(path))
                return;

            // Resolve any drive letter to its real UNC path before storing it,
            // so the list means the same thing regardless of which drive
            // letter (if any) a given user has that share mapped to.
            string resolved = ProtectedRootsStore.ResolveMappedDriveToUnc(path);

            if (!resolved.StartsWith(@"\\", StringComparison.Ordinal))
            {
                DialogResult confirm = MessageBox.Show(
                    this,
                    "This doesn't look like a network path, and won't necessarily mean the " +
                    "same folder for every user on this computer. Add it anyway?",
                    "Not a network path",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);

                if (confirm != DialogResult.Yes)
                    return;
            }

            if (!_rootsListBox.Items.Contains(resolved))
                _rootsListBox.Items.Add(resolved);

            _pathTextBox.Clear();
        }

        private void RemoveButton_Click(object sender, EventArgs e)
        {
            if (_rootsListBox.SelectedItem != null)
                _rootsListBox.Items.Remove(_rootsListBox.SelectedItem);
        }

        private void SaveButton_Click(object sender, EventArgs e)
        {
            var roots = new List<string>();
            foreach (object item in _rootsListBox.Items)
                roots.Add(item.ToString());

            try
            {
                ProtectedRootsStore.SetProtectedRoots(roots);
                MessageBox.Show(
                    this,
                    "Saved. Changes take effect immediately - no restart needed.",
                    "Folder Move Protector",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (UnauthorizedAccessException)
            {
                MessageBox.Show(
                    this,
                    "Couldn't save - this needs to run as Administrator.",
                    "Folder Move Protector",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }
    }
}
