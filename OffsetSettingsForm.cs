using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SDK_HD.Dim
{
    /// <summary>Hộp thoại "Fit Crop View - Offset Grid/Level".</summary>
    internal class OffsetSettingsForm : Form
    {
        private readonly FitCropSettings _settings;
        private readonly TextBox _hTail = new TextBox();
        private readonly TextBox _hBubble = new TextBox();
        private readonly TextBox _vTail = new TextBox();
        private readonly TextBox _vBubble = new TextBox();

        private static readonly Color Blue = Color.FromArgb(52, 112, 172);
        private static readonly Color Red = Color.FromArgb(196, 64, 52);

        public OffsetSettingsForm(FitCropSettings settings)
        {
            _settings = settings;

            Text = "Fit Crop View - Offset Grid/Level";
            Font = new Font("Segoe UI", 9f);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(440, 552);

            // Mô tả
            Label intro = new Label();
            intro.Text = "Đầu mỗi Grid/Level vươn ra ngoài mép Crop bao xa. 2000 = 2000mm, 2m = 2000mm.";
            intro.SetBounds(20, 16, 400, 40);
            Controls.Add(intro);

            // Hình minh hoạ
            DiagramPanel diagram = new DiagramPanel();
            diagram.SetBounds(20, 62, 400, 186);
            Controls.Add(diagram);

            // Tiêu đề cột
            AddHeader("Đầu không bubble (Tail)", 140, 262);
            AddHeader("Đầu có bubble (Bubble)", 285, 262);

            // Hàng Ngang / Đứng
            AddRowLabel("Ngang", Blue, 296);
            AddRowLabel("Đứng", Red, 336);

            AddBox(_hTail, 140, 296, _settings.HorizontalTailMm);
            AddBox(_hBubble, 285, 296, _settings.HorizontalBubbleMm);
            AddBox(_vTail, 140, 336, _settings.VerticalTailMm);
            AddBox(_vBubble, 285, 336, _settings.VerticalBubbleMm);

            // Giải thích
            Label note = new Label();
            note.ForeColor = Color.FromArgb(90, 90, 90);
            note.Font = new Font("Segoe UI", 8.5f);
            note.Text =
                "Mỗi đầu được xét riêng: đầu có bubble dùng offset Bubble, đầu không có bubble dùng offset Tail. " +
                "Ngang/Đứng tính theo hướng trên màn hình: trong view mặt cắt Level là ngang, Grid là đứng; " +
                "Grid xiên tính theo hướng gần hơn. " +
                "Offset đo tới đầu đường Grid/Level, bubble được vẽ thêm ra ngoài nên hãy chừa chỗ cho nó.";
            note.SetBounds(20, 384, 400, 104);
            Controls.Add(note);

            // Nút
            Button cancel = new Button();
            cancel.Text = "Huỷ";
            cancel.DialogResult = DialogResult.Cancel;
            cancel.SetBounds(230, 502, 90, 32);
            Controls.Add(cancel);

            Button save = new Button();
            save.Text = "Lưu";
            save.SetBounds(330, 502, 90, 32);
            save.Click += OnSave;
            Controls.Add(save);

            AcceptButton = save;
            CancelButton = cancel;
        }

        private void AddHeader(string text, int x, int y)
        {
            Label l = new Label();
            l.Text = text;
            l.TextAlign = ContentAlignment.MiddleCenter;
            l.SetBounds(x, y, 130, 28);
            Controls.Add(l);
        }

        private void AddRowLabel(string text, Color color, int y)
        {
            Label l = new Label();
            l.Text = text;
            l.ForeColor = color;
            l.Font = new Font("Segoe UI", 10f);
            l.TextAlign = ContentAlignment.MiddleLeft;
            l.SetBounds(20, y, 110, 28);
            Controls.Add(l);
        }

        private void AddBox(TextBox box, int x, int y, double valueMm)
        {
            box.Text = FitCropSettings.FormatLength(valueMm);
            box.TextAlign = HorizontalAlignment.Center;
            box.SetBounds(x, y, 130, 28);
            Controls.Add(box);
        }

        private void OnSave(object sender, EventArgs e)
        {
            double hTail, hBubble, vTail, vBubble;

            if (!Check(_hTail, out hTail)) return;
            if (!Check(_hBubble, out hBubble)) return;
            if (!Check(_vTail, out vTail)) return;
            if (!Check(_vBubble, out vBubble)) return;

            _settings.HorizontalTailMm = hTail;
            _settings.HorizontalBubbleMm = hBubble;
            _settings.VerticalTailMm = vTail;
            _settings.VerticalBubbleMm = vBubble;

            DialogResult = DialogResult.OK;
        }

        private bool Check(TextBox box, out double mm)
        {
            if (FitCropSettings.TryParseLength(box.Text, out mm)) return true;

            MessageBox.Show(this,
                "Giá trị \"" + box.Text + "\" không hợp lệ.\nNhập số theo mm (2500) hoặc kèm đơn vị (2.5m, 250cm, 2500mm).",
                Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            box.Focus();
            box.SelectAll();
            return false;
        }

        /// <summary>Hình minh hoạ: khung Crop nét đứt, 1 đường ngang (xanh) và 1 đường đứng (đỏ).</summary>
        private class DiagramPanel : Panel
        {
            public DiagramPanel()
            {
                DoubleBuffered = true;
                ResizeRedraw = true;
                BackColor = Color.White;
                BorderStyle = BorderStyle.FixedSingle;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

                float W = ClientSize.Width;
                float H = ClientSize.Height;
                const float R = 9f; // bán kính bubble

                // Khung Crop (nét đứt)
                RectangleF crop = new RectangleF(W * 0.29f, H * 0.24f, W * 0.41f, H * 0.50f);
                using (Pen dash = new Pen(Color.FromArgb(150, 150, 150), 1f))
                {
                    dash.DashStyle = DashStyle.Dash;
                    g.DrawRectangle(dash, crop.X, crop.Y, crop.Width, crop.Height);
                }
                using (Font f = new Font("Segoe UI", 8f, FontStyle.Italic))
                using (Brush b = new SolidBrush(Color.FromArgb(150, 150, 150)))
                {
                    g.DrawString("crop", f, b, crop.X + 4, crop.Y + 2);
                }

                // Đường ngang: đầu trái không bubble (gạch tick), đầu phải có bubble "A"
                float yH = H * 0.50f;
                float xL = W * 0.08f;
                float xBubble = W * 0.91f;
                using (Pen p = new Pen(Blue, 2f))
                using (Pen thin = new Pen(Blue, 1.6f))
                {
                    g.DrawLine(p, xL, yH, xBubble - R, yH);
                    g.DrawLine(p, xL, yH - 7, xL, yH + 7);
                    g.DrawEllipse(thin, xBubble - R, yH - R, 2 * R, 2 * R);
                }
                DrawCenteredText(g, "A", Blue, xBubble, yH);

                // Đường đứng: đầu trên có bubble "1", đầu dưới không bubble
                float xV = W * 0.50f;
                float yTop = H * 0.11f;
                float yBottom = H * 0.93f;
                using (Pen p = new Pen(Red, 2f))
                using (Pen thin = new Pen(Red, 1.6f))
                {
                    g.DrawLine(p, xV, yTop + R, xV, yBottom);
                    g.DrawLine(p, xV - 7, yBottom, xV + 7, yBottom);
                    g.DrawEllipse(thin, xV - R, yTop - R, 2 * R, 2 * R);
                }
                DrawCenteredText(g, "1", Red, xV, yTop);

                // Nhãn
                using (Font f = new Font("Segoe UI", 8.5f))
                {
                    using (Brush b = new SolidBrush(Blue))
                        g.DrawString("horizontal", f, b, xL - 2, yH + 8);
                    using (Brush b = new SolidBrush(Red))
                        g.DrawString("vertical", f, b, xV + 10, H * 0.74f);
                }
            }

            private static void DrawCenteredText(Graphics g, string text, Color color, float cx, float cy)
            {
                using (Font f = new Font("Segoe UI", 7.5f))
                using (Brush b = new SolidBrush(color))
                using (StringFormat sf = new StringFormat())
                {
                    sf.Alignment = StringAlignment.Center;
                    sf.LineAlignment = StringAlignment.Center;
                    g.DrawString(text, f, b, cx, cy, sf);
                }
            }
        }
    }
}
