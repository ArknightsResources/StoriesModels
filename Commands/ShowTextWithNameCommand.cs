﻿namespace ArknightsResources.Stories.Models.Commands
{
    /// <summary>
    /// 表示一个显示剧情文本(有名称)的命令
    /// </summary>
    public class ShowTextWithNameCommand : StoryCommand
    {
        /// <summary>
        /// 使用给定的参数初始化<see cref="ShowTextWithNameCommand"/>类的新实例
        /// </summary>
        /// <param name="name">剧情人物名称</param>
        /// <param name="text">剧情文本</param>
        public ShowTextWithNameCommand(string name, string text) : base(false)
        {
            Name = name;
            Text = text;
        }

        /// <summary>
        /// 剧情人物名称
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// 剧情文本
        /// </summary>
        public string Text { get; }

        /// <inheritdoc/>
        protected override string GetDebuggerDisplay() => $"[{GetType().Name}] {Name}:{Text}";
    }
}
