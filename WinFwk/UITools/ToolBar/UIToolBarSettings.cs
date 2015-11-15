﻿using System.Drawing;

namespace WinFwk.UITools.ToolBar
{
    public class UIToolBarSettings
    {
        public static readonly UIToolBarSettings File = new UIToolBarSettings("File", int.MaxValue, Properties.Resources.small_folder_vertical_open);
        public static readonly UIToolBarSettings Help = new UIToolBarSettings("Help", int.MinValue, Properties.Resources.small_help);

        public string Name { get; }
        public int Priority { get; }
        public Bitmap Icon 
        {
            get;
        }

        public UIToolBarSettings(string name, int priority, Bitmap icon)
        {
            Name = name;
            Priority = priority;
            Icon = icon;
        }
    }
}