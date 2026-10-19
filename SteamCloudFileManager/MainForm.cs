using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.IO;

namespace SteamCloudFileManager
{
    public partial class MainForm : Form
    {
        /// <summary>If set (e.g. from the command line with /appid), the app connects automatically when the form is shown.</summary>
        internal string StartupAppId;

        IRemoteStorage storage;
        System.Windows.Forms.Timer steamCallbackTimer;
        bool busy;

        public MainForm()
        {
            InitializeComponent();
            InitTimer();
            this.FormClosed += MainForm_FormClosed;
        }

        public MainForm(string startupAppId) : this()
        {
            StartupAppId = startupAppId;
        }

        void InitTimer()
        {
            // Steamworks needs its callbacks pumped regularly; without this the file list may not
            // appear until the user manually refreshes (see readme). One WinForms timer tick keeps
            // the API up to date on the UI thread. Fully qualified because 'Timer' is ambiguous
            // with System.Threading.Timer.
            steamCallbackTimer = new System.Windows.Forms.Timer();
            steamCallbackTimer.Interval = 250;
            steamCallbackTimer.Tick += steamCallbackTimer_Tick;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (!string.IsNullOrEmpty(StartupAppId) && string.IsNullOrWhiteSpace(appIdTextBox.Text))
            {
                appIdTextBox.Text = StartupAppId;
                connectButton.PerformClick();
            }
        }

        void steamCallbackTimer_Tick(object sender, EventArgs e)
        {
            RemoteStorage rs = storage as RemoteStorage;
            if (rs == null || rs.IsDisposed || busy) return;
            try { Steamworks.SteamAPI.RunCallbacks(); }
            catch { /* the API can briefly be unusable during shutdown; ignore ticks until Refresh */ }
        }

        void MainForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            steamCallbackTimer.Stop();
            if (storage != null)
            {
                IDisposable disposable = storage as IDisposable;
                if (disposable != null)
                {
                    try { disposable.Dispose(); }
                    catch { /* nothing we can do while shutting down */ }
                }
                storage = null;
            }
        }

        /// <summary>Disables buttons and shows the wait cursor while a long operation runs.</summary>
        void SetBusy(bool value)
        {
            busy = value;
            UseWaitCursor = value;
            connectButton.Enabled = !value;
            refreshButton.Enabled = !value && storage != null;
            downloadButton.Enabled = !value && storage != null && remoteListView.SelectedIndices.Count == 1;
            deleteButton.Enabled = !value && storage != null && remoteListView.SelectedIndices.Count > 0;
            uploadButton.Enabled = !value && storage != null;
            backupAllButton.Enabled = !value && storage != null && remoteListView.Items.Count > 0;
        }

        private void connectButton_Click(object sender, EventArgs e)
        {
            if (busy) return;
            uint appId;
            if (string.IsNullOrWhiteSpace(appIdTextBox.Text))
            {
                MessageBox.Show(this, "Please enter an App ID.", "Failed to connect", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            if (!uint.TryParse(appIdTextBox.Text.Trim(), out appId))
            {
                MessageBox.Show(this, "Please make sure the App ID you entered is valid.", "Failed to connect", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            if (appId == 0)
            {
                MessageBox.Show(this, "App ID 0 is not valid. Look up the game's App ID on steamdb.info.", "Failed to connect", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            try
            {
                storage = RemoteStorage.CreateInstance(appId);
                steamCallbackTimer.Start();

                if (!storage.IsCloudEnabledForAccount)
                {
                    MessageBox.Show(this, "Steam Cloud is disabled for your account. To enable it, open Steam's Settings and turn on Steam Cloud under Cloud.", "Warning", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                }

                refreshButton.Enabled = true;
                SetBusy(false);
                refreshButton_Click(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Failed to connect", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void refreshButton_Click(object sender, EventArgs e)
        {
            if (busy) return;
            if (storage == null)
            {
                MessageBox.Show(this, "Not connected", "Error", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                return;
            }

            try
            {
                List<IRemoteFile> files = storage.GetFiles();
                remoteListView.Items.Clear();
                foreach (IRemoteFile file in files)
                {
                    remoteListView.Items.Add(new ListViewItem(new string[] {
                        file.Name,
                        file.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"),
                        file.Size.ToString(),
                        file.IsPersisted.ToString(),
                        file.Exists.ToString(),
                        file.SyncPlatforms.ToString()
                    }) { Tag = file });
                }
                updateQuota();
                SetBusy(false);

                bool anyFile = remoteListView.Items.Count > 0;
                backupAllButton.Enabled = anyFile;
                if (!anyFile)
                {
                    // Distinguish an empty cloud from a failed listing so the user knows
                    // whether they are connected to the right app.
                    quotaLabel.Text = "Connected - 0 cloud files for this app";
                }
                downloadButton.Enabled = deleteButton.Enabled = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Can't refresh." + Environment.NewLine + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
            }
        }

        void updateQuota()
        {
            if (storage == null) throw new InvalidOperationException("Not connected");
            int totalBytes, availBytes;
            if (!storage.GetQuota(out totalBytes, out availBytes))
            {
                quotaLabel.Text = "Quota unavailable";
                return;
            }
            long used = (long)totalBytes - availBytes;
            quotaLabel.Text = string.Format("{0} / {1} used ({2} files)",
                FormatBytes(used), FormatBytes(totalBytes), remoteListView.Items.Count);
        }

        static readonly string[] SizeUnits = { "B", "KB", "MB", "GB" };

        static string FormatBytes(long bytes)
        {
            double size = bytes;
            int unit = 0;
            while (size >= 1024.0 && unit < SizeUnits.Length - 1)
            {
                size /= 1024.0;
                unit++;
            }
            string text;
            if (unit == 0) text = size.ToString("0"); // whole bytes look cleaner as integers
            else text = size.ToString("0.#");
            return text + " " + SizeUnits[unit];
        }

        /// <summary>Turns a Steam cloud path ('/' separated, may contain sub directories) into a
        /// usable local path inside <paramref name="baseDir"/>, creating sub directories as needed.</summary>
        static string BuildLocalPath(string baseDir, string cloudName)
        {
            string relative = cloudName.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            string[] parts = relative.Split(Path.DirectorySeparatorChar);
            string result = baseDir;
            foreach (string part in parts)
            {
                if (part.Length == 0) continue;
                result = Path.Combine(result, SanitizeFileName(part));
            }
            string parent = Path.GetDirectoryName(Path.GetFullPath(result));
            if (!string.IsNullOrEmpty(parent) && !Directory.Exists(parent))
            {
                Directory.CreateDirectory(parent);
            }
            return result;
        }

        static string SanitizeFileName(string name)
        {
            string result = name;
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                result = result.Replace(c, '_');
            }
            return result;
        }

        private void downloadButton_Click(object sender, EventArgs e)
        {
            if (busy) return;
            if (storage == null)
            {
                MessageBox.Show(this, "Not connected", "Error", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                return;
            }
            if (remoteListView.SelectedIndices.Count != 1)
            {
                MessageBox.Show(this, "Please select only one file.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                return;
            }

            IRemoteFile file = remoteListView.SelectedItems[0].Tag as IRemoteFile;
            saveFileDialog1.FileName = SanitizeFileName(Path.GetFileName(file.Name));
            if (saveFileDialog1.ShowDialog(this) == System.Windows.Forms.DialogResult.OK)
            {
                try
                {
                    SetBusy(true);
                    byte[] data = file.ReadAllBytes();
                    File.WriteAllBytes(saveFileDialog1.FileName, data);
                    MessageBox.Show(this, "File downloaded.", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "File download failed." + Environment.NewLine + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                }
                finally
                {
                    SetBusy(false);
                }
            }
        }

        /// <summary>Bulk downloads every file currently listed. Runs on a thread pool thread so the
        /// UI stays responsive; progress is marshalled back through Invoke.</summary>
        private void backupAllButton_Click(object sender, EventArgs e)
        {
            if (busy) return;
            if (storage == null)
            {
                MessageBox.Show(this, "Not connected", "Error", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                return;
            }
            if (remoteListView.Items.Count == 0)
            {
                MessageBox.Show(this, "There are no files to back up.", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            List<IRemoteFile> files = new List<IRemoteFile>();
            foreach (ListViewItem item in remoteListView.Items)
            {
                IRemoteFile file = item.Tag as IRemoteFile;
                if (file != null) files.Add(file);
            }

            DialogResult result = backupFolderDialog.ShowDialog(this);
            if (result != System.Windows.Forms.DialogResult.OK) return;
            string baseDir = backupFolderDialog.SelectedPath;

            string folderName = SanitizeFileName("steamcloud_" + appIdTextBox.Text.Trim());
            string targetDir = Path.Combine(baseDir, folderName);
            try { Directory.CreateDirectory(targetDir); }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not create backup folder." + Environment.NewLine + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                return;
            }

            SetBusy(true);
            quotaLabel.Text = "Backing up 0/" + files.Count + "...";

            ThreadPool.QueueUserWorkItem(delegate
            {
                int ok = 0, failed = 0;
                List<string> failedNames = new List<string>();

                for (int i = 0; i < files.Count; i++)
                {
                    IRemoteFile file = files[i];
                    string progress = "Backing up " + (i + 1) + "/" + files.Count + ": " + file.Name;
                    try
                    {
                        string destination = BuildLocalPath(targetDir, file.Name);
                        byte[] data = file.ReadAllBytes();
                        File.WriteAllBytes(destination, data);
                        ok++;
                    }
                    catch
                    {
                        failed++;
                        failedNames.Add(file.Name);
                    }

                    try { this.BeginInvoke((MethodInvoker)delegate { if (!IsDisposed) quotaLabel.Text = progress; }); }
                    catch (ObjectDisposedException) { break; }
                    catch (InvalidOperationException) { break; } // form handle destroyed mid backup
                }

                string summary = files.Count + " files processed: " + ok + " saved, " + failed + " failed."
                    + Environment.NewLine + "Backup folder: " + targetDir;
                if (failedNames.Count > 0)
                {
                    summary += Environment.NewLine + Environment.NewLine + "Failed files:" + Environment.NewLine + string.Join(Environment.NewLine, failedNames.ToArray());
                }

                try { this.BeginInvoke((MethodInvoker)delegate
                {
                    if (IsDisposed) return;
                    SetBusy(false);
                    MessageBox.Show(this, summary, ok == files.Count ? "Backup complete" : "Backup finished with errors",
                        MessageBoxButtons.OK, failed == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Exclamation);
                    refreshButton_Click(this, EventArgs.Empty);
                }); }
                catch (ObjectDisposedException) { /* form closed while running */ }
                catch (InvalidOperationException) { /* form closed while running */ }
            });
        }

        /// <summary>Uploads a local file into the app's Steam Cloud storage. This is the counterpart
        /// to Download and makes save restoration possible, not just inspection.</summary>
        private void uploadButton_Click(object sender, EventArgs e)
        {
            if (busy) return;
            if (storage == null)
            {
                MessageBox.Show(this, "Not connected", "Error", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                return;
            }

            DialogResult result = uploadFileDialog.ShowDialog(this);
            if (result != System.Windows.Forms.DialogResult.OK) return;

            string localPath = uploadFileDialog.FileName;
            string remoteName = Path.GetFileName(localPath);

            // Reuse the existing cloud file object when possible so the original case of the
            // name is preserved (GetFile lowercases names, which could create a duplicate file).
            IRemoteFile target = null;
            string compare = remoteName.ToLowerInvariant();
            foreach (ListViewItem item in remoteListView.Items)
            {
                IRemoteFile candidate = item.Tag as IRemoteFile;
                if (candidate != null && candidate.Name.ToLowerInvariant() == compare)
                {
                    target = candidate;
                    break;
                }
            }
            if (target == null)
            {
                // No matching entry in the list; let the storage create one from the plain name.
                target = storage.GetFile(remoteName);
            }

            if (target != null && target.Exists)
            {
                DialogResult overwrite = MessageBox.Show(this,
                    remoteName + " already exists in the cloud." + Environment.NewLine +
                    "Upload and overwrite it?", "Confirm overwrite", MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation);
                if (overwrite == System.Windows.Forms.DialogResult.No) return;

                // Safety net: keep a local copy of the cloud state before overwriting it.
                try
                {
                    string backupPath = localPath + ".cloud-backup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".bak";
                    File.WriteAllBytes(backupPath, target.ReadAllBytes());
                    MessageBox.Show(this, "A copy of the current cloud version was saved to:" + Environment.NewLine + backupPath,
                        "Backup created", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    DialogResult proceed = MessageBox.Show(this,
                        "Could not create a backup of the existing cloud file: " + ex.Message + Environment.NewLine + Environment.NewLine +
                        "Continue without backup?", "Backup failed", MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation);
                    if (proceed == System.Windows.Forms.DialogResult.No) return;
                }
            }

            bool uploaded = false;
            try
            {
                SetBusy(true);
                byte[] data = File.ReadAllBytes(localPath);
                uploaded = target.WriteAllBytes(data);
                if (uploaded)
                {
                    MessageBox.Show(this, "File uploaded to the Steam Cloud.", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    MessageBox.Show(this, "The upload was rejected by the Steam API (out of quota or file too big?).", "Upload failed", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "File upload failed." + Environment.NewLine + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
            }
            finally
            {
                SetBusy(false);
            }
            if (uploaded) refreshButton_Click(this, EventArgs.Empty);
        }

        private void deleteButton_Click(object sender, EventArgs e)
        {
            if (busy) return;
            if (storage == null)
            {
                MessageBox.Show(this, "Not connected", "Error", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                return;
            }
            if (remoteListView.SelectedIndices.Count == 0)
            {
                MessageBox.Show(this, "Please select files to delete.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                return;
            }

            string fileSummary = remoteListView.SelectedIndices.Count == 1
                ? remoteListView.SelectedItems[0].SubItems[0].Text
                : remoteListView.SelectedIndices.Count + " files";
            if (MessageBox.Show(this, "Are you sure you want to delete " + fileSummary + " from the Steam Cloud?", "Confirm deletion", MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation) == System.Windows.Forms.DialogResult.No) return;

            bool allSuccess = true;

            foreach (ListViewItem item in remoteListView.SelectedItems)
            {
                IRemoteFile file = item.Tag as IRemoteFile;
                try
                {
                    bool success = file.Delete();
                    if (!success)
                    {
                        allSuccess = false;
                        MessageBox.Show(this, file.Name + " failed to delete.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                    }
                    else
                    {
                        item.Remove();
                    }
                }
                catch (Exception ex)
                {
                    // An exception during delete is also a failure; previously allSuccess stayed
                    // true here and the user was told "Files deleted." anyway.
                    allSuccess = false;
                    MessageBox.Show(this, file.Name + " failed to delete." + Environment.NewLine + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                }
            }

            try { updateQuota(); }
            catch
            {
                // Quota is informational only; a failure here must not be an unhandled exception.
                quotaLabel.Text = "Quota unavailable";
            }
            if (allSuccess && remoteListView.Items.Count == 0) backupAllButton.Enabled = false;
            if (allSuccess) MessageBox.Show(this, "Files deleted.", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void remoteListView_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (!busy)
            {
                downloadButton.Enabled = (storage != null && remoteListView.SelectedIndices.Count == 1);
                deleteButton.Enabled = (storage != null && remoteListView.SelectedIndices.Count > 0);
            }
        }
    }
}
