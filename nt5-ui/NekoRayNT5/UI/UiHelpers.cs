using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using NekoRayNT5.App;

namespace NekoRayNT5.UI
{
    // Places "label: control" rows one under another. The dialogs are built in code;
    // their appearance is deliberately plain until the cosmetic stage. The label column grows when a
    // label (for example a translated one) is wider than planned; long captions wrap.
    internal sealed class RowLayout
    {
        private const int RowHeight = 27;
        private const int Margin = 12;
        private const int LabelGap = 8;

        private readonly Control _parent;
        private int _labelWidth;
        private readonly int _controlWidth;
        private int _y = Margin;
        // controls of the second column: moved when the label column grows
        private readonly System.Collections.Generic.List<Control> _column = new System.Collections.Generic.List<Control>();

        public RowLayout(Control parent, int labelWidth, int controlWidth)
        {
            _parent = parent;
            _labelWidth = labelWidth;
            _controlWidth = controlWidth;
        }

        public int Bottom
        {
            get { return _y; }
        }

        public int Width
        {
            get { return Margin * 2 + _labelWidth + _controlWidth; }
        }

        public Label Caption(string text)
        {
            Label label = new Label();
            label.AutoSize = true;
            label.Text = text;
            label.Location = new Point(Margin, _y + 3);
            _parent.Controls.Add(label);
            int max = Width - Margin * 2;
            label.MaximumSize = new Size(max, 0);
            int height = label.GetPreferredSize(new Size(max, 0)).Height;
            _y += Math.Max(RowHeight - 4, height + 6);
            return label;
        }

        private void Place(string label, Control control, int height)
        {
            Label l = new Label();
            l.AutoSize = true;
            l.Text = label;
            l.Location = new Point(Margin, _y + 3);
            _parent.Controls.Add(l);
            Widen(l.PreferredWidth + LabelGap);
            control.Location = new Point(Margin + _labelWidth, _y);
            control.Width = _controlWidth;
            _parent.Controls.Add(control);
            _column.Add(control);
            _y += height;
        }

        // Moves the second column to the right when a label needs more room.
        private void Widen(int needed)
        {
            if (needed <= _labelWidth)
            {
                return;
            }
            int delta = needed - _labelWidth;
            _labelWidth = needed;
            foreach (Control c in _column)
            {
                c.Left += delta;
            }
        }

        public TextBox Text(string label, int maxLength)
        {
            TextBox box = new TextBox();
            box.MaxLength = maxLength;
            Place(label, box, RowHeight);
            return box;
        }

        public TextBox Multiline(string label, int height)
        {
            TextBox box = new TextBox();
            box.Multiline = true;
            box.AcceptsReturn = true;
            box.ScrollBars = ScrollBars.Vertical;
            box.WordWrap = false;
            box.Height = height;
            Place(label, box, height + 7);
            return box;
        }

        public ComboBox Combo(string label, string[] items, bool editable)
        {
            ComboBox combo = new ComboBox();
            combo.DropDownStyle = editable ? ComboBoxStyle.DropDown : ComboBoxStyle.DropDownList;
            combo.Items.AddRange(items);
            Place(label, combo, RowHeight);
            return combo;
        }

        public NumericUpDown Number(string label, int min, int max)
        {
            NumericUpDown number = new NumericUpDown();
            number.Minimum = min;
            number.Maximum = max;
            Place(label, number, RowHeight);
            number.Width = 80;
            return number;
        }

        public CheckBox Check(string text)
        {
            CheckBox check = new CheckBox();
            check.AutoSize = true;
            check.Text = text;
            check.Location = new Point(Margin + _labelWidth, _y + 2);
            _parent.Controls.Add(check);
            _column.Add(check);
            _y += RowHeight - 3;
            return check;
        }

        // Adds OK / Cancel at the bottom of a form and sizes the form around the rows.
        public static Button AddDialogButtons(Form form, int width, int bottom, string okText)
        {
            Button ok = new Button();
            ok.Text = okText;
            ok.Size = new Size(75, 23);
            ok.Location = new Point(width - 12 - 75 - 6 - 75, bottom + 8);
            Button cancel = new Button();
            cancel.Text = Lang.T("Cancel");
            cancel.Size = new Size(75, 23);
            cancel.Location = new Point(width - 12 - 75, bottom + 8);
            cancel.DialogResult = DialogResult.Cancel;
            form.Controls.Add(ok);
            form.Controls.Add(cancel);
            form.AcceptButton = ok;
            form.CancelButton = cancel;
            form.ClientSize = new Size(width, bottom + 8 + 23 + 12);
            form.FormBorderStyle = FormBorderStyle.FixedDialog;
            form.MaximizeBox = false;
            form.MinimizeBox = false;
            form.ShowInTaskbar = false;
            form.StartPosition = FormStartPosition.CenterParent;
            return ok;
        }

        public static void SelectItem(ComboBox combo, string value)
        {
            int index = combo.Items.IndexOf(value);
            if (index >= 0)
            {
                combo.SelectedIndex = index;
            }
            else if (combo.DropDownStyle == ComboBoxStyle.DropDown)
            {
                combo.Text = value;
            }
            else if (combo.Items.Count > 0)
            {
                combo.SelectedIndex = 0;
            }
        }
    }

    // The program has no icon resource yet; a small state-coloured dot is drawn at run time
    // (grey - stopped, green - running, blue - running with system proxy, red - error).
    internal static class IconFactory
    {
        public static Icon Create(Color color)
        {
            using (Bitmap bitmap = new Bitmap(16, 16))
            {
                using (Graphics g = Graphics.FromImage(bitmap))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);
                    using (Brush brush = new SolidBrush(color))
                    {
                        g.FillEllipse(brush, 1, 1, 13, 13);
                    }
                    using (Pen pen = new Pen(Color.FromArgb(64, 64, 64)))
                    {
                        g.DrawEllipse(pen, 1, 1, 13, 13);
                    }
                }
                // The handle is owned by the returned icon for the life of the program.
                return Icon.FromHandle(bitmap.GetHicon());
            }
        }
    }
}
