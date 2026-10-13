using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Security.Cryptography;
using System.Windows.Forms;

namespace VpnSifreleri
{
    /// <summary>
    /// The cockpit: name, user name and password boxes on top, the list below. Enter adds, or updates the entry picked
    /// from the list. The rules live in PasswordStore; this window only reports what they decided.
    /// </summary>
    internal sealed class CockpitForm : Form
    {
        /// <summary>A hidden password in the list; the same for every password, so lengths do not show.</summary>
        internal const string Hidden = "••••••••";

        private const string OneLineOnly = "Ad, kullanıcı adı ve şifre tek satır olmalı";

        private static readonly Color OkColor = Color.FromArgb(15, 123, 15);
        private static readonly Color ErrorColor = Color.FromArgb(196, 43, 28);

        internal readonly SingleLineBox NameBox = new SingleLineBox();
        internal readonly SingleLineBox UsernameBox = new SingleLineBox();
        internal readonly SingleLineBox PasswordBox = new SingleLineBox();
        internal readonly Button SubmitButton = new Button();
        internal readonly Button CancelEditButton = new Button();
        internal readonly CheckBox RevealBox = new CheckBox();
        internal readonly ListView EntryList = new ListView();
        internal readonly Button DeleteButton = new Button();
        internal readonly Label StatusLabel = new Label();

        /// <summary>Asks a yes/no question; tests answer it themselves.</summary>
        internal Func<string, bool> Confirm;
        /// <summary>Reports a failed save; tests collect the message.</summary>
        internal Action<string> ShowError;

        private readonly PasswordStore store;
        private readonly float scale;
        private readonly Font uiFont = SystemFonts.MessageBoxFont;
        private readonly Icon windowIcon = AppIcon.ForWindow();
        private readonly ColumnHeader nameColumn;
        private readonly ColumnHeader usernameColumn;
        private readonly ColumnHeader passwordColumn;
        private string editing;  // name of the entry in the boxes; null while adding
        private bool refreshing; // selection changes made by code, not by the user

        public CockpitForm(PasswordStore store)
        {
            this.store = store;
            Confirm = AskYesNo;
            ShowError = ShowErrorBox;
            using (Graphics screen = Graphics.FromHwnd(IntPtr.Zero))
                scale = screen.DpiX / 96f;

            Text = "VPN Şifreleri";
            Icon = windowIcon;
            Font = uiFont;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(Px(620), Px(440));
            MinimumSize = new Size(Px(500), Px(320));

            NameBox.AccessibleName = "Müşteri";
            NameBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            UsernameBox.AccessibleName = "Kullanıcı adı";
            UsernameBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            PasswordBox.AccessibleName = "Şifre";
            PasswordBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            PasswordBox.UseSystemPasswordChar = true;
            SetUpButton(SubmitButton, "Ekle");
            SetUpButton(CancelEditButton, "Vazgeç");
            CancelEditButton.Visible = false;
            RevealBox.Text = "Göster";
            RevealBox.AutoSize = true;

            FlowLayoutPanel buttons = new FlowLayoutPanel();
            buttons.AutoSize = true;
            buttons.WrapContents = false;
            buttons.Margin = Padding.Empty;
            buttons.Controls.Add(SubmitButton);
            buttons.Controls.Add(CancelEditButton);

            TableLayoutPanel inputs = new TableLayoutPanel();
            inputs.Dock = DockStyle.Fill;
            inputs.AutoSize = true;
            inputs.ColumnCount = 4;
            inputs.RowCount = 3;
            inputs.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
            inputs.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
            inputs.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
            inputs.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            inputs.Controls.Add(Caption("Müşteri"), 0, 0);
            inputs.Controls.Add(Caption("Kullanıcı adı"), 1, 0);
            inputs.Controls.Add(Caption("Şifre"), 2, 0);
            inputs.Controls.Add(NameBox, 0, 1);
            inputs.Controls.Add(UsernameBox, 1, 1);
            inputs.Controls.Add(PasswordBox, 2, 1);
            inputs.Controls.Add(buttons, 3, 1);
            inputs.Controls.Add(RevealBox, 2, 2);

            EntryList.AccessibleName = "Kayıtlar";
            EntryList.Dock = DockStyle.Fill;
            EntryList.Margin = new Padding(3, Px(8), 3, Px(8));
            EntryList.View = View.Details;
            EntryList.FullRowSelect = true;
            EntryList.MultiSelect = false;
            EntryList.HideSelection = false;
            EntryList.HeaderStyle = ColumnHeaderStyle.Nonclickable;
            nameColumn = EntryList.Columns.Add("Müşteri", Px(220));
            usernameColumn = EntryList.Columns.Add("Kullanıcı adı", Px(150));
            passwordColumn = EntryList.Columns.Add("Şifre", Px(160));
            // Before the list joins the layout, so its very first size already fits the column
            EntryList.ClientSizeChanged += delegate { FitPasswordColumn(); };

            StatusLabel.AutoSize = true;
            StatusLabel.Anchor = AnchorStyles.Left;
            StatusLabel.UseMnemonic = false; // "B&B" shows as written, not as an underlined B
            SetUpButton(DeleteButton, "Sil");

            TableLayoutPanel footer = new TableLayoutPanel();
            footer.Dock = DockStyle.Fill;
            footer.AutoSize = true;
            footer.ColumnCount = 2;
            footer.RowCount = 1;
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            footer.Controls.Add(StatusLabel, 0, 0);
            footer.Controls.Add(DeleteButton, 1, 0);

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.Padding = new Padding(Px(12));
            root.ColumnCount = 1;
            root.RowCount = 3;
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Controls.Add(inputs, 0, 0);
            root.Controls.Add(EntryList, 0, 1);
            root.Controls.Add(footer, 0, 2);
            Controls.Add(root);

            AcceptButton = SubmitButton;
            SubmitButton.Click += delegate { Submit(); };
            CancelEditButton.Click += delegate { ResetToAdd(); NameBox.Focus(); };
            DeleteButton.Click += delegate { DeleteSelected(); };
            RevealBox.CheckedChanged += delegate { ApplyReveal(); };
            EntryList.SelectedIndexChanged += OnListSelectionChanged;
            EntryList.KeyDown += OnListKeyDown;
            foreach (SingleLineBox box in new[] { NameBox, UsernameBox, PasswordBox })
                box.PasteRefused += delegate { ShowStatus(OneLineOnly, true); };
            store.Changed += OnStoreChanged;

            RefreshList();
            ResetToAdd();
        }

        /// <summary>Name of the entry being updated, or null while adding.</summary>
        internal string EditingName
        {
            get { return editing; }
        }

        /// <summary>Ekle / Güncelle, also Enter.</summary>
        internal void Submit()
        {
            string name = NameBox.Text, username = UsernameBox.Text, password = PasswordBox.Text;
            string original = editing;
            string done = original == null ? "eklendi" : "güncellendi";
            StoreResult result;
            if (!TryChange(() => original == null ? store.Add(name, username, password) : store.Update(original, name, username, password), out result)) return;

            if (result == StoreResult.NameExists && original == null)
            {
                // Adding a name that is already there: offer to replace what the boxes hold, keep the stored spelling
                Entry existing = store.Find(name);
                string question = existing.Username == PasswordStore.Clean(username)
                    ? existing.Name + " zaten var. Şifresi güncellensin mi?"
                    : existing.Name + " zaten var. Kullanıcı adı ve şifresi güncellensin mi?";
                if (!Confirm(question)) return;
                name = existing.Name;
                done = "güncellendi";
                if (!TryChange(() => store.Update(existing.Name, existing.Name, username, password), out result)) return;
            }

            switch (result)
            {
                case StoreResult.Ok:
                    Entry saved = store.Find(name);
                    ResetToAdd();
                    ShowStatus("✓ " + saved.Name + " " + done, false);
                    ScrollTo(saved);
                    NameBox.Focus();
                    break;
                case StoreResult.NameEmpty:
                    ShowStatus("Müşteri adı boş olamaz", true);
                    NameBox.Focus();
                    break;
                case StoreResult.PasswordEmpty:
                    ShowStatus("Şifre boş olamaz", true);
                    PasswordBox.Focus();
                    break;
                case StoreResult.MultiLine:
                    ShowStatus(OneLineOnly, true);
                    break;
                case StoreResult.NameExists:
                    ShowStatus("Bu isimde başka bir kayıt var", true);
                    NameBox.Focus();
                    break;
                default:
                    ResetToAdd();
                    ShowStatus("Kayıt bulunamadı", true);
                    break;
            }
        }

        /// <summary>Puts a listed entry in the boxes for updating.</summary>
        internal void BeginEdit(Entry entry)
        {
            editing = entry.Name;
            NameBox.Text = entry.Name;
            UsernameBox.Text = entry.Username;
            PasswordBox.Text = entry.Password;
            SubmitButton.Text = "Güncelle";
            CancelEditButton.Visible = true;
            DeleteButton.Enabled = true;
            ShowStatus("", false);
        }

        /// <summary>Back to adding: empty boxes, nothing picked in the list. Leaves the focus where it is.</summary>
        internal void ResetToAdd()
        {
            editing = null;
            NameBox.Text = "";
            UsernameBox.Text = "";
            PasswordBox.Text = "";
            SubmitButton.Text = "Ekle";
            CancelEditButton.Visible = false;
            DeleteButton.Enabled = false;
            refreshing = true;
            try
            {
                EntryList.SelectedItems.Clear();
            }
            finally
            {
                refreshing = false;
            }
        }

        /// <summary>Sil, also Delete in the list; asks first.</summary>
        internal void DeleteSelected()
        {
            string name = editing;
            if (name == null || !Confirm(name + " silinsin mi?")) return;
            StoreResult result;
            if (!TryChange(() => store.Remove(name), out result)) return;
            ResetToAdd();
            ShowStatus("✓ " + name + " silindi", false);
            NameBox.Focus();
        }

        /// <summary>Esc: stops updating, or closes the window when already adding.</summary>
        internal void HandleEscape()
        {
            if (editing == null)
            {
                Close();
                return;
            }
            ResetToAdd();
            NameBox.Focus();
        }

        /// <summary>The window is being brought forward, maybe onto a shared screen: passwords go back to hidden.</summary>
        internal void HidePasswords()
        {
            RevealBox.Checked = false;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData != Keys.Escape) return base.ProcessCmdKey(ref msg, keyData);
            HandleEscape();
            return true;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) store.Changed -= OnStoreChanged;
            base.Dispose(disposing);
            if (disposing)
            {
                windowIcon.Dispose();
                uiFont.Dispose();
            }
        }

        // Runs one store change. A failed save is shown and the boxes keep what was typed.
        private bool TryChange(Func<StoreResult> change, out StoreResult result)
        {
            result = StoreResult.NotFound;
            try
            {
                result = change();
                return true;
            }
            catch (IOException ex) { ShowError("Kaydedilemedi: " + ex.Message); }
            catch (UnauthorizedAccessException ex) { ShowError("Kaydedilemedi: " + ex.Message); }
            catch (CryptographicException ex) { ShowError("Kaydedilemedi: " + ex.Message); }
            return false;
        }

        private void RefreshList()
        {
            refreshing = true;
            EntryList.BeginUpdate();
            try
            {
                EntryList.Items.Clear();
                foreach (Entry entry in store.Entries)
                {
                    ListViewItem item = new ListViewItem(new[] { entry.Name, entry.Username, PasswordText(entry) });
                    item.Tag = entry;
                    item.Selected = editing != null && PasswordStore.NameComparer.Equals(entry.Name, editing);
                    EntryList.Items.Add(item);
                }
            }
            finally
            {
                EntryList.EndUpdate();
                refreshing = false;
            }
        }

        private void ApplyReveal()
        {
            PasswordBox.UseSystemPasswordChar = !RevealBox.Checked;
            foreach (ListViewItem item in EntryList.Items)
                item.SubItems[2].Text = PasswordText((Entry)item.Tag);
        }

        private string PasswordText(Entry entry)
        {
            return RevealBox.Checked ? entry.Password : Hidden;
        }

        private void ScrollTo(Entry entry)
        {
            foreach (ListViewItem item in EntryList.Items)
            {
                if (item.Tag != entry) continue;
                item.EnsureVisible();
                return;
            }
        }

        private void ShowStatus(string text, bool error)
        {
            StatusLabel.Text = text;
            StatusLabel.ForeColor = error ? ErrorColor : OkColor;
        }

        private void OnStoreChanged(object sender, EventArgs e)
        {
            RefreshList();
        }

        private void OnListSelectionChanged(object sender, EventArgs e)
        {
            if (refreshing) return;
            if (EntryList.SelectedItems.Count == 1) BeginEdit((Entry)EntryList.SelectedItems[0].Tag);
            else ResetToAdd();
        }

        private void OnListKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Delete) return;
            DeleteSelected();
            e.Handled = true;
        }

        // The password column takes the width the other two leave
        private void FitPasswordColumn()
        {
            passwordColumn.Width = Math.Max(Px(80), EntryList.ClientSize.Width - nameColumn.Width - usernameColumn.Width - 1);
        }

        private bool AskYesNo(string question)
        {
            return MessageBox.Show(this, question, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
        }

        private void ShowErrorBox(string message)
        {
            MessageBox.Show(this, message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private void SetUpButton(Button button, string text)
        {
            button.Text = text;
            button.AutoSize = true;
            button.MinimumSize = new Size(Px(84), 0);
            button.UseVisualStyleBackColor = true;
        }

        private static Label Caption(string text)
        {
            Label label = new Label();
            label.Text = text;
            label.AutoSize = true;
            label.Margin = new Padding(3, 3, 3, 0);
            return label;
        }

        private int Px(int logical)
        {
            return (int)Math.Round(logical * scale);
        }
    }
}
