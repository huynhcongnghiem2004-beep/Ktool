using System;
using System.Drawing;
using System.Windows.Forms;

namespace SDK_HD.Dim
{
    /// <summary>Tuỳ chọn Auto Tag (nhớ giá trị lần dùng trước trong cùng phiên Revit).</summary>
    internal class AutoTagOptions
    {
        public bool Columns = true;
        public bool Beams = true;
        public bool Foundations = true;
        public bool Floors = true;
        public bool SkipTagged = true;
        public bool UseLeader = false;

        public static readonly AutoTagOptions Last = new AutoTagOptions();
    }

    /// <summary>Chủ sở hữu cửa sổ là cửa sổ chính của Revit (để hộp thoại không bị che).</summary>
    internal class RevitWindowOwner : IWin32Window
    {
        public IntPtr Handle { get; private set; }

        public RevitWindowOwner(IntPtr handle)
        {
            Handle = handle;
        }
    }

    /// <summary>Hộp thoại chọn loại cấu kiện cần Tag.</summary>
    internal class AutoTagForm : Form
    {
        private readonly AutoTagOptions _o;
        private readonly CheckBox _columns = new CheckBox();
        private readonly CheckBox _beams = new CheckBox();
        private readonly CheckBox _foundations = new CheckBox();
        private readonly CheckBox _floors = new CheckBox();
        private readonly CheckBox _skipTagged = new CheckBox();
        private readonly CheckBox _leader = new CheckBox();

        public AutoTagForm(AutoTagOptions options, int viewCount, int limitedCount)
        {
            _o = options;

            Text = "Auto Tag kết cấu";
            Font = new Font("Segoe UI", 9f);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(380, 360);

            string scope = viewCount > 1
                ? "Số view sẽ xử lý: " + viewCount + "."
                : "Xử lý view đang mở.";
            if (limitedCount > 0)
                scope += "\nChỉ Tag " + limitedCount + " cấu kiện đang chọn.";

            Label head = new Label();
            head.Text = scope + "\nChọn loại cấu kiện cần đánh Tag:";
            head.SetBounds(20, 14, 340, 48);
            Controls.Add(head);

            AddCheck(_columns, "Cột (Structural Columns)", 70, _o.Columns);
            AddCheck(_beams, "Dầm (Structural Framing)", 98, _o.Beams);
            AddCheck(_foundations, "Móng (Structural Foundations)", 126, _o.Foundations);
            AddCheck(_floors, "Sàn (Floors)", 154, _o.Floors);

            Label line = new Label();
            line.BorderStyle = BorderStyle.Fixed3D;
            line.SetBounds(20, 190, 340, 2);
            Controls.Add(line);

            AddCheck(_skipTagged, "Bỏ qua cấu kiện đã có Tag trong view", 204, _o.SkipTagged);
            AddCheck(_leader, "Dùng leader (đường dẫn) cho Tag", 232, _o.UseLeader);

            Label note = new Label();
            note.ForeColor = Color.FromArgb(90, 90, 90);
            note.Font = new Font("Segoe UI", 8.5f);
            note.Text = "Tag dùng family Tag mặc định của từng loại cấu kiện. " +
                        "Cần nạp sẵn family Tag tương ứng vào project.";
            note.SetBounds(20, 268, 340, 40);
            Controls.Add(note);

            Button cancel = new Button();
            cancel.Text = "Huỷ";
            cancel.DialogResult = DialogResult.Cancel;
            cancel.SetBounds(170, 316, 90, 32);
            Controls.Add(cancel);

            Button ok = new Button();
            ok.Text = "Tag";
            ok.SetBounds(270, 316, 90, 32);
            ok.Click += OnOk;
            Controls.Add(ok);

            AcceptButton = ok;
            CancelButton = cancel;
        }

        private void AddCheck(CheckBox box, string text, int y, bool value)
        {
            box.Text = text;
            box.Checked = value;
            box.AutoSize = false;
            box.SetBounds(24, y, 336, 24);
            Controls.Add(box);
        }

        private void OnOk(object sender, EventArgs e)
        {
            if (!_columns.Checked && !_beams.Checked && !_foundations.Checked && !_floors.Checked)
            {
                MessageBox.Show(this, "Hãy chọn ít nhất một loại cấu kiện.", Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _o.Columns = _columns.Checked;
            _o.Beams = _beams.Checked;
            _o.Foundations = _foundations.Checked;
            _o.Floors = _floors.Checked;
            _o.SkipTagged = _skipTagged.Checked;
            _o.UseLeader = _leader.Checked;

            DialogResult = DialogResult.OK;
        }
    }
}
