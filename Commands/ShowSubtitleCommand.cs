using System;
using System.Collections.Generic;
using System.Text;

namespace ArknightsResources.Stories.Models.Commands
{
    public class ShowSubtitleCommand : StoryCommand
    {
        //TODO:add comment
        public ShowSubtitleCommand(string text, double x, double y, string alignment, double size, double delay, double width) : base(false)
        {
            Text = text;
            X = x;
            Y = y;
            Alignment = alignment;
            Size = size;
            Delay = delay;
            Width = width;
        }

        public string Text { get; }

        public double X { get; }

        public double Y { get; }

        public string Alignment { get; }

        public double Size { get; }

        public double Delay { get; }

        public double Width { get; }
    }
}
