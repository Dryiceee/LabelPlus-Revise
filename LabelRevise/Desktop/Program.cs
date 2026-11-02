// Copyright 2015, Noodlefighter. Based on LabelPlus; GPL-2.0.
using System;
using System.IO;
using System.Windows.Forms;

namespace LabelPlus
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Directory.SetCurrentDirectory(Application.StartupPath);
            System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("en-US");
            Application.Run(new MainFrm());
        }
    }
}
