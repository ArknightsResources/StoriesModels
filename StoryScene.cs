﻿using ArknightsResources.Stories.Models.Commands;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace ArknightsResources.Stories.Models
{
    /// <summary>
    /// 表示一个剧情
    /// </summary>
    public sealed class StoryScene : IEnumerable<StoryCommand>
    {
        internal StoryScene()
        {
            //For internal use
        }

        /// <summary>
        /// 使用给定的参数构造<see cref="StoryScene"/>的新实例
        /// </summary>
        /// <param name="storyCommands">包含剧情命令的数组</param>
        /// <param name="isSkippable">指示剧情是否可跳过</param>
        /// <param name="isAutoable">指示剧情是否可自动播放</param>
        /// <param name="fitMode">剧情的填充模式</param>
        /// <param name="comment">剧情文件的注释</param>
        public StoryScene(StoryCommand[] storyCommands,
                          bool isSkippable,
                          bool isAutoable,
                          string fitMode,
                          string comment = "")
        {
            StoryCommands = storyCommands;
            IsSkippable = isSkippable;
            IsAutoable = isAutoable;
            FitMode = fitMode;
            Comment = comment;
        }

        /// <summary>
        /// 一个剧情内所包含的命令
        /// </summary>
        public StoryCommand[] StoryCommands { get; internal set; }

        /// <summary>
        /// 指示剧情是否可跳过
        /// </summary>
        public bool IsSkippable { get; internal set; }

        /// <summary>
        /// 指示剧情是否可自动播放
        /// </summary>
        public bool IsAutoable { get; internal set; }

        /// <summary>
        /// 剧情的填充模式
        /// </summary>
        public string FitMode { get; internal set; }

        /// <summary>
        /// 剧情文件的注释
        /// </summary>
        public string Comment { get; internal set; }

        /// <summary>
        /// 获取剧情文件的纯文本形式
        /// </summary>
        /// <returns>剧情文件的纯文本</returns>
        public string GetStoryText()
        {
            StringBuilder builder = new StringBuilder(StoryCommands.Length);
            GetTextInternal(StoryCommands, builder);
            return builder.ToString();
        }

        private static void GetTextInternal(IEnumerable<StoryCommand> cmds, StringBuilder builder, bool cmdInDecision = false)
        {
            //转换为List<T>的原因是:接下来的某些操作需要获取之后的项目,而IEnumerable不能进行这些操作
            List<StoryCommand> commands = cmds.ToList();

            List<StoryCommand> textCommands = (from textCmd
                                               in commands
                                               where textCmd is TextCommand || textCmd is DecisionCommand
                                               select textCmd).ToList();

            foreach (var item in textCommands)
            {
                if (cmdInDecision)
                {
                    _ = builder.Append('\t');
                }

                switch (item)
                {
                    case ShowTextWithNameCommand textWithNameCmd:
                        _ = builder.AppendLine($"{textWithNameCmd.Name}: {textWithNameCmd.Text}");
                        break;
                    case ShowStickerCommand showStickerCmd:
                        var matchPlainText = Regex.Match(showStickerCmd.Text, @"<[\s\S]*>([\s\S]*)<\/[\s\S]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                        if (matchPlainText.Success)
                        {
                            var stickerPlainText = matchPlainText.Groups[1].Value;
                            _ = builder.AppendLine(stickerPlainText);
                        }
                        else
                        {
                            _ = builder.AppendLine(showStickerCmd.Text);
                        }
                        break;
                    case TextCommand textCommand:
                        _ = builder.AppendLine(textCommand.Text);
                        break;
                    case DecisionCommand decisionCmd:
                        //添加空行来分隔选择文本与其他对话
                        builder.AppendLine();
                        foreach (var option in decisionCmd.AvailableOptions)
                        {
                            builder.AppendLine($"[{option}]");
                            GetTextInternal(decisionCmd[option], builder, true);
                        }

                        int indexInTextCmds = textCommands.IndexOf(item);
                        //如果下一条命令是DecisionCommand,则结束switch语句,避免写入多余的空行
                        if (indexInTextCmds != -1 && textCommands.ElementAtOrDefault(indexInTextCmds + 1) is DecisionCommand)
                        {
                            break;
                        }
                        builder.AppendLine();
                        break;
                    default:
                        break;
                }

                int index = commands.IndexOf(item);
                if (index != -1)
                {
                    //从完整的StoryCommand列表中选取从当前TextCommand到下一个TextCommand中的命令
                    IEnumerable<StoryCommand> cmdSegment = commands.Skip(index + 1)
                                                                   .TakeWhile((cmd) => !(cmd is TextCommand));

                    //如果当前命令是DecisionCommand,则不会进行下面的操作,因为我们在前面已经添加了空行
                    //接下来,如果cmdSegment中有DecisionCommand,操作同上
                    //最后,如果cmdSegment中有HideDialogCommand及ShowBackgroundCommand,才添加空行
                    if (!(item is DecisionCommand)
                        && !cmdSegment.Any((cmd) => cmd is DecisionCommand)
                        && cmdSegment.Any((cmd) => cmd is HideDialogCommand)
                        && cmdSegment.Any((cmd) => cmd is ShowBackgroundCommand
                                                   || cmd is ShowCharacterIllustrationCommand))
                    {
                        builder.AppendLine();
                    }
                }
            }
        }

        /// <inheritdoc/>
        public IEnumerator<StoryCommand> GetEnumerator()
        {
            return ((IEnumerable<StoryCommand>)StoryCommands).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return StoryCommands.GetEnumerator();
        }
    }
}
