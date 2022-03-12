using System;
using System.Collections.Generic;
using System.Text;

namespace ArknightsResources.Stories.Models.Commands
{
    /// <summary>
    /// 表示一个显示剧情文本(无名称)的命令
    /// </summary>
    public class ShowPlainTextCommand : StoryCommand
    {
        /// <summary>
        /// 使用剧情文本初始化<seealso cref="ShowPlainTextCommand"/>类的新实例
        /// </summary>
        /// <param name="text">剧情文本</param>
        public ShowPlainTextCommand(string text) : base(false)
        {
            Text = text;
        }

        /// <summary>
        /// 剧情文本
        /// </summary>
        public string Text { get; }

        /// <inheritdoc/>
        protected override string GetDebuggerDisplay() => $"[{GetType().Name}]{Text}";
    }
}
