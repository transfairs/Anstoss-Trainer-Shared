using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows.Forms;

namespace AnstossTrainer.Shared
{
    /// <summary>
    /// Base for every trainer view: knows the shared game memory and provides field clearing,
    /// numeric input validation, the "is the game attached" check and link handling. Game-specific
    /// view bases (e.g. Anstoss 2's EntityView) derive from it.
    /// </summary>
    public class TrainerView : UserControl
    {
        /// <summary>The game memory this view reads and writes.</summary>
        internal ProcessMemory Memory { get => this.memory; private set => this.memory = value; }
        private ProcessMemory memory;

        /// <summary>Parameterless, for the WinForms designer only.</summary>
        protected TrainerView()
        {
        }

        /// <summary>A view on the given game memory.</summary>
        protected TrainerView(ProcessMemory memory)
        {
            this.memory = memory;
        }

        /// <summary>Name of the game process in the "not found" message, e.g. "Anstoss-2-Gold".</summary>
        protected virtual string GameDisplayName => "Anstoss";

        /// <summary>Recursively blanks every input control under <paramref name="parent"/>, except those in <paramref name="exclude"/>.</summary>
        protected void ClearAllFields(Control parent, ISet<Control> exclude = null)
        {
            foreach (Control ctrl in parent.Controls)
            {
                if (exclude != null && exclude.Contains(ctrl))
                    continue;

                if (ctrl is TextBox tb)
                    tb.Clear();
                else if (ctrl is ComboBox cb)
                {
                    cb.SelectedIndex = -1;
                    // For an editable (DropDown-style) combo, SelectedIndex alone doesn't blank the
                    // visible text - only a DropDownList combo auto-clears its text that way.
                    cb.Text = string.Empty;
                }
                else if (ctrl is CheckBox chk)
                    chk.Checked = false;
                else if (ctrl is RadioButton rb)
                    rb.Checked = false;
                else if (ctrl.HasChildren)
                    ClearAllFields(ctrl, exclude);
            }
        }

        /// <summary>Whether the game process is currently attached; optionally shows an error dialog if not.</summary>
        protected bool IsGameRunning(bool silent = false)
        {
            if (this.memory.mProc.Process == null)
            {
                if (!silent)
                    System.Windows.Forms.MessageBox.Show(this, $"{this.GameDisplayName}-Prozess nicht gefunden", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }

            return true;
        }

        /// <summary>KeyPress handler that lets only digits and control keys through.</summary>
        protected void NumericOnly_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar))
            {
                e.Handled = true;
            }
        }
        /// <summary>TextChanged handler that caps the text at 65535.</summary>
        protected void UshortMaxNumber_TextChanged(object sender, EventArgs e)
        {
            var textBox = (TextBox)sender;
            if (!string.IsNullOrEmpty(textBox.Text) && !ushort.TryParse(textBox.Text, out _))
            {
                textBox.Text = "65535";
                textBox.SelectionStart = textBox.Text.Length;
            }
        }
        /// <summary>TextChanged handler that caps the text at 2147483647.</summary>
        protected void IntMaxNumber_TextChanged(object sender, EventArgs e)
        {
            var textBox = (TextBox)sender;
            if (!string.IsNullOrEmpty(textBox.Text) && !int.TryParse(textBox.Text, out _))
            {
                textBox.Text = "2147483647";
                textBox.SelectionStart = textBox.Text.Length;
            }
        }
        /// <summary>TextChanged handler that caps the text at 255.</summary>
        protected void ByteMax255_TextChanged(object sender, EventArgs e)
        {
            var textBox = (TextBox)sender;
            if (!string.IsNullOrEmpty(textBox.Text) && !byte.TryParse(textBox.Text, out _))
            {
                textBox.Text = "255";
                textBox.SelectionStart = textBox.Text.Length;
            }
        }
        /// <summary>Opens the clicked link's URL (its LinkData).</summary>
        protected void LinkLabel_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            string url = e.Link.LinkData as string;
            if (!string.IsNullOrEmpty(url))
            {
                this.StartProcess(url);
            }
        }

        // Extracted so tests can override the actual OS process launch (which would otherwise pop
        // open a real browser) while still exercising LinkLabel_LinkClicked's own logic for real.
        /// <summary>Starts the given URL; tests override it so no browser opens.</summary>
        internal virtual void StartProcess(string url) => Process.Start(url);
    }
}
