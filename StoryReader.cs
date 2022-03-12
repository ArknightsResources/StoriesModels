﻿#pragma warning disable IDE0075 // 简化条件表达式

using ArknightsResources.Stories.Models.Commands;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace ArknightsResources.Stories.Models
{
    /// <summary>
    /// 阅读明日方舟剧情文件的类
    /// </summary>
    public sealed class StoryReader
    {
        private readonly StringReader stringReader;
        private readonly bool convertPopupDialog;
        private int textLine = 0;
        private const string MatchDecimalString = @"([1-9]\d*\.?\d*|0\d*\.?\d*|\d)";

        /// <summary>
        /// 使用原始剧情文本初始化<seealso cref="StoryReader"/>的新实例
        /// </summary>
        /// <param name="storyText">原始剧情文本</param>
        /// <param name="nickName">博士的名字</param>
        /// <param name="convertPopupDialog">是否将PopupDialog转换为ShowPlainTextCommand</param>
        /// <exception cref="ArgumentException"/>
        public StoryReader(string storyText, string nickName = "", bool convertPopupDialog = false)
        {
            if (string.IsNullOrWhiteSpace(storyText))
            {
                throw new ArgumentException($"“{nameof(storyText)}”不能为 null 或空白。", nameof(storyText));
            }
            if (nickName is null)
            {
                throw new ArgumentNullException(nameof(nickName));
            }

            if (storyText.Contains("{@nickname}"))
            {
                StringBuilder sb = new StringBuilder(storyText);
                sb.Replace("{@nickname}", nickName);
                stringReader = new StringReader(sb.ToString());
            }
            else
            {
                stringReader = new StringReader(storyText);
            }

            this.convertPopupDialog = convertPopupDialog;
        }

        /// <summary>
        /// 获取从原始剧情文本转换而来的<seealso cref="StoryScene"/>实例
        /// </summary>
        /// <returns>一个<seealso cref="StoryScene"/>实例,其表示原始剧情文本</returns>
        public StoryScene GetStoryScene()
        {
            StoryScene scene = new StoryScene();

            List<StoryCommand> storyCommands = new List<StoryCommand>();
            bool IsHeaderInfoAdded = false;
            while (true)
            {
                if (stringReader.Peek() == -1)
                {
                    break;
                }
                string strToAnalyse = ReadText();
                if (strToAnalyse.StartsWith("["))
                {
                    var matchHeader = GetMatchByPattern(strToAnalyse, "HEADER");
                    if (IsHeaderInfoAdded == false && matchHeader.Success)
                    {
                        //读取文件的HEADER信息

                        var matchTutorial = GetMatchByPattern(strToAnalyse, "is_tutorial=true");
                        if (matchTutorial.Success)
                        {
                            throw new ArgumentException("无法分析包含教程的剧情文件");
                        }

                        var matchSkippable = GetMatchByPattern(strToAnalyse, "is_skippable=(true|false)");
                        if (matchSkippable.Success)
                        {
                            string value = matchSkippable.Groups[1].Value;
                            scene.IsSkippable = bool.Parse(value);
                        }
                        var matchAutoable = GetMatchByPattern(strToAnalyse, "is_autoable=(true|false)");
                        if (matchAutoable.Success)
                        {
                            string value = matchAutoable.Groups[1].Value;
                            scene.IsAutoable = bool.Parse(value);
                        }
                        var matchFitMode = GetMatchByPattern(strToAnalyse, @"fit_mode=""([\s\S]*)""");
                        if (matchFitMode.Success)
                        {
                            string value = matchFitMode.Groups[1].Value;
                            scene.FitMode = value;
                        }
                        var matchComment = GetMatchByPattern(strToAnalyse, @"\)\] ([\s\S]*)");
                        if (matchComment.Success)
                        {
                            string comment = matchComment.Groups[1].Value;
                            scene.Comment = comment;
                        }
                        IsHeaderInfoAdded = true;
                    }
                    else
                    {
                        //匹配Decision
                        Match matchDecison = GetMatchByPattern(strToAnalyse, @"\[Decision\(options=""([\s\S]*)"", values=""([\s\S]*)""\)\]");
                        if (matchDecison.Success)
                        {
                            #region MatchDecision
                            string[] options = matchDecison.Groups[1].Value.Split(';');
                            string[] values = matchDecison.Groups[2].Value.Split(';');

                            //键是选项(如“结果怎么样？”“......”“我的脑袋又热又胀，很不舒服。”)
                            //值为各选项分支的命令(如”[name="凯尔希"]并没有什么新的进展。“)
                            Dictionary<string, StoryCommand[]> result = new Dictionary<string, StoryCommand[]>(values.Length);

                            //当前要处理的选项
                            List<string> currentKeys = null;

                            //用于临时放置各选项分支命令
                            List<StoryCommand> temp = new List<StoryCommand>(5);
                            while (true)
                            {
                                string str = ReadText();
                                Match matchPredicate = GetMatchByPattern(str, @"\[Predicate\(references=""([\s\S]*)""\)\]");
                                if (matchPredicate.Success)
                                {
                                    string[] references = matchPredicate.Groups[1].Value.Split(';');
                                    if (references.SequenceEqual(values))
                                    {
                                        /*
                                         * 这种情况的例子:[Decision(options="XXX;YYY;ZZZ", values="1;2;3")]
                                         * .......
                                         * [Predicate(references="1;2;3")]
                                        */

                                        if (result.Count == 0)
                                        {
                                            foreach (var item in options)
                                            {
                                                result[item] = new StoryCommand[] { new NoOperationCommand() };
                                            }
                                        }
                                        else
                                        {
                                            foreach (var item in currentKeys)
                                            {
                                                result[item] = temp.ToArray();
                                                temp.Clear();
                                            }
                                        }

                                        break;
                                    }

                                    if (currentKeys is null)
                                    {
                                        InitCurrentKeys(out currentKeys, options, references);
                                    }
                                    else
                                    {
                                        temp.Insert(0, new ShowDialogCommand());
                                        foreach (string item in currentKeys)
                                        {
                                            result[item] = temp.ToArray();
                                        }
                                        temp.Clear();
                                        InitCurrentKeys(out currentKeys, options, references);
                                    }
                                    continue;
                                }
                                else
                                {
                                    //解析每一行文本所代表的命令
                                    StoryCommand storyCommand = AnalyseCommandText(str);
                                    temp.Add(storyCommand);
                                }
                            }

                            DecisionCommand decisionCommand = new DecisionCommand(result);
                            storyCommands.Add(decisionCommand);
                            continue;
                            #endregion
                        }
                        else
                        {
                            StoryCommand storyCommand = AnalyseCommandText(strToAnalyse);
                            storyCommands.Add(storyCommand);
                        }
                    }
                }
                else
                {
                    //无"[",说明为纯剧情文本
                    if (string.IsNullOrWhiteSpace(strToAnalyse) || strToAnalyse.StartsWith("//"))
                    {
                        //但如果为空白或者包含注释,就跳过这次循环迭代
                        continue;
                    }
                    StoryCommand textCommand = new ShowPlainTextCommand(strToAnalyse);
                    storyCommands.Add(textCommand);
                }
            }

            scene.StoryCommands = storyCommands.ToArray();
            return scene;

            void InitCurrentKeys(out List<string> currentKey, string[] options, string[] references)
            {
                currentKey = new List<string>(options.Length);
                foreach (var item in references)
                {
                    int index = int.Parse(item);
                    currentKey.Add(options[index - 1]);
                }
            }
        }

        private string ReadText()
        {
            string strToAnalyse = stringReader.ReadLine();
            textLine++;
            return strToAnalyse;
        }

        private StoryCommand AnalyseCommandText(string strToAnalyse)
        {
            // TODO: Add more...
            #region MatchStopMusic
            {
                var matchStopMusic = GetMatchByPattern(strToAnalyse, @"\[stopmusic\(([\s\S]*)\)\]");
                if (matchStopMusic.Success)
                {
                    string stopMusicArgs = matchStopMusic.Groups[1].Value;
                    var matchVolume = GetMatchByPattern(stopMusicArgs, $"volume=({MatchDecimalString})");
                    double fadeTime = matchVolume.Success ? GetDoubleFromMatch(matchVolume) : 0d;
                    StopMusicCommand stopMusicCommandWithArg = new StopMusicCommand(fadeTime);
                    return stopMusicCommandWithArg;
                }
                else if (GetMatchByPattern(strToAnalyse,@"\[stopmusic\]").Success)
                {
                    StopMusicCommand stopMusicCommand = new StopMusicCommand(0d);
                    return stopMusicCommand;
                }
            }
            #endregion
            #region MatchPlayMusic
            {
                var matchPlayMusic = GetMatchByPattern(strToAnalyse, @"\[playMusic\(([\s\S]*)\)\]");
                if (matchPlayMusic.Success)
                {
                    string playMusicArgs = matchPlayMusic.Groups[1].Value;
                    Match matchIntro = GetMatchByPattern(playMusicArgs, @"intro=""([\s\S]*?)""");
                    Match matchLoopOrKey = GetMatchByPattern(playMusicArgs, @"key=""([\s\S]*?)""");
                    Match matchVolume = GetMatchByPattern(playMusicArgs, $"volume={MatchDecimalString}");
                    Match matchFadeTime = GetMatchByPattern(playMusicArgs, $"fadetime={MatchDecimalString}");
                    Match matchDelay = GetMatchByPattern(playMusicArgs, $"delay={MatchDecimalString}");

                    double volume = matchVolume.Success ? GetDoubleFromMatch(matchVolume) : 0.4d;
                    double fadeTime = matchFadeTime.Success ? GetDoubleFromMatch(matchFadeTime) : 0d;
                    double delay = matchDelay.Success ? GetDoubleFromMatch(matchDelay) : 0;
                    if (matchIntro.Success)
                    {
                        //有Intro和Key
                        string intro = matchIntro.Groups[1].Value;
                        string loop = matchLoopOrKey.Groups[1].Value;

                        StoryCommand playMusicCommand = new PlayMusicCommand(intro, loop, volume, fadeTime, delay);
                        return playMusicCommand;
                    }
                    else
                    {
                        //无Loop
                        string key = matchLoopOrKey.Groups[1].Value;
                        StoryCommand playMusicCommand = new PlayMusicCommand(key, volume, fadeTime, delay);
                        return playMusicCommand;
                    }
                }
            }
            #endregion
            #region MatchDelay
            {
                var matchDelayWithArg = GetMatchByPattern(strToAnalyse, $@"\[Delay\(time={MatchDecimalString}\)\]");
                if (matchDelayWithArg.Success)
                {
                    double delay = GetDoubleFromMatch(matchDelayWithArg);
                    DelayCommand delayCommandWithArg = new DelayCommand(delay);
                    return delayCommandWithArg;
                }
                else if (GetMatchByPattern(strToAnalyse, "Delay").Success)
                {
                    DelayCommand delayCommand = new DelayCommand(0d);
                    return delayCommand;
                }
            }
            #endregion
            #region MatchTextWithName
            {
                var matchName = GetMatchByPattern(strToAnalyse, @"\[name=""([\s\S]*)""\]");
                var matchText = GetMatchByPattern(strToAnalyse, @"\]\s*([\s\S]*)");
                if (matchName.Success)
                {
                    string name = matchName.Groups[1].Value;
                    string text = matchText.Groups[1].Value;
                    ShowTextWithNameCommand textCommand = new ShowTextWithNameCommand(name, text);
                    return textCommand;
                }
            }
            #endregion
            #region MatchCharacter
            {
                //示例文本:[Character(name="avg_npc_142#1",name2="char_455_nothing_1#3",enter2="right",fadetime=1)]
                var matchCharacter = GetMatchByPattern(strToAnalyse, @"\[Character\(([\s\S]*)\)\]");
                if (matchCharacter.Success)
                {
                    string charactorArgs = matchCharacter.Groups[1].Value;
                    var matchCodeName = GetMatchByPattern(charactorArgs, @"name=""([\s\S]*)""");
                    var matchCodeName2 = GetMatchByPattern(charactorArgs, @"name2=""([\s\S]*)""");
                    var matchFadeTime = GetMatchByPattern(charactorArgs, $"fadetime={MatchDecimalString}");
                    var matchEnterStyle = GetMatchByPattern(charactorArgs, @"enter=""(left|right|none)""");
                    var matchEnterStyle2 = GetMatchByPattern(charactorArgs, @"enter2=""(left|right|none)""");
                    var matchBlock = GetMatchByPattern(charactorArgs, "block=(true|false)");
                    var matchFocus = GetMatchByPattern(charactorArgs, @"focus=([\d])");

                    string codeName = matchCodeName.Success ? matchCodeName.Groups[1].Value : string.Empty;
                    string codeName2 = matchCodeName2.Success ? matchCodeName2.Groups[1].Value : string.Empty;
                    bool isBlock = matchBlock.Success ? GetBooleanFromMatch(matchBlock) : false;
                    int focus = matchFocus.Success ? int.Parse(matchFocus.Groups[1].Value) : 1;
                    double fadeTime = matchFadeTime.Success ? GetDoubleFromMatch(matchFadeTime) : 0.15d;
                    CharacterIllustrationEnterStyle enterStyle = matchEnterStyle.Success ? (CharacterIllustrationEnterStyle)Enum.Parse(
                        typeof(CharacterIllustrationEnterStyle), matchEnterStyle.Groups[1].Value) : CharacterIllustrationEnterStyle.None;
                    CharacterIllustrationEnterStyle enterStyle2 = matchEnterStyle2.Success ? (CharacterIllustrationEnterStyle)Enum.Parse(
                        typeof(CharacterIllustrationEnterStyle), matchEnterStyle2.Groups[1].Value) : CharacterIllustrationEnterStyle.None;

                    if (string.IsNullOrEmpty(codeName2))
                    {
                        ShowCharacterIllustrationCommand illustrationCommand = new ShowCharacterIllustrationCommand(codeName, fadeTime, isBlock, enterStyle);
                        return illustrationCommand;
                    }
                    else
                    {
                        ShowCharacterIllustrationCommand illustrationCommand = new ShowCharacterIllustrationCommand(codeName,
                            codeName2, fadeTime, isBlock, focus, enterStyle, enterStyle2);
                        return illustrationCommand;
                    }
                }
                else
                {
                    var matchHideCharacter = GetMatchByPattern(strToAnalyse, @"\[character\]");
                    if (matchHideCharacter.Success)
                    {
                        HideCharacterIllustrationCommand hideCharacterCommand = new HideCharacterIllustrationCommand();
                        return hideCharacterCommand;
                    }
                }
            }
            #endregion
            #region MatchHideDialog
            {
                var matchHideDialog = GetMatchByPattern(strToAnalyse, @"\[dialog\(([\s\S]*)\)\]");
                if (matchHideDialog.Success)
                {
                    string args = matchHideDialog.Groups[1].Value;
                    var matchFadeTime = GetMatchByPattern(args, $@"fadetime={MatchDecimalString}");
                    var matchBlock = GetMatchByPattern(args, @"block=(true|false)");

                    double fadeTime = matchFadeTime.Success ? GetDoubleFromMatch(matchFadeTime) : 0;
                    bool isBlock = matchBlock.Success ? GetBooleanFromMatch(matchBlock) : false;
                    HideDialogCommand hideDialogCommand = new HideDialogCommand(fadeTime, isBlock);
                    return hideDialogCommand;
                }
                else if (GetMatchByPattern(strToAnalyse,@"\[dialog\]").Success)
                {
                    return new HideDialogCommand(0d, false);
                }
            }
            #endregion
            #region MatchBlocker
            {
                var matchBlocker = GetMatchByPattern(strToAnalyse, @"\[Blocker\(([\s\S]*)\)\]");
                if (matchBlocker.Success)
                {
                    string blockerArgs = matchBlocker.Groups[1].Value;

                    var matchAlpha = GetMatchByPattern(blockerArgs, $"a={MatchDecimalString}");
                    var matchR = GetMatchByPattern(blockerArgs, $"r={MatchDecimalString}");
                    var matchG = GetMatchByPattern(blockerArgs, $"g={MatchDecimalString}");
                    var matchB = GetMatchByPattern(blockerArgs, $"b={MatchDecimalString}");
                    var matchFadeTime = GetMatchByPattern(blockerArgs, $"fadetime={MatchDecimalString}");
                    var matchBlock = GetMatchByPattern(blockerArgs, "block=(true|false)");
                    
                    double a = matchAlpha.Success ? GetDoubleFromMatch(matchAlpha) : 1;
                    double r = matchR.Success ? GetDoubleFromMatch(matchR) : 0;
                    double g = matchG.Success ? GetDoubleFromMatch(matchG) : 0;
                    double b = matchB.Success ? GetDoubleFromMatch(matchB) : 0;
                    double fadeTime = matchFadeTime.Success ? GetDoubleFromMatch(matchFadeTime) : 0.2;
                    bool isBlock = GetBooleanFromMatch(matchBlock);
                    ShowBlockerCommand blockerCommand = new ShowBlockerCommand(a, r, g, b, fadeTime, isBlock);
                    return blockerCommand;
                }
            }
            #endregion
            #region MatchBackground
            {
                var matchBackground = GetMatchByPattern(strToAnalyse, @"\[Background\(([\s\S]*)\)\]");
                if (matchBackground.Success)
                {
                    string matchBackgroundArgs = matchBackground.Groups[1].Value;
                    var matchImageCodename = GetMatchByPattern(matchBackgroundArgs, @"image=""([\s\S]*?)""");
                    var matchScreenAdaptMode = GetMatchByPattern(matchBackgroundArgs, @"screenadapt=""([\s\S]*?)""");
                    var matchFadeTime = GetMatchByPattern(matchBackgroundArgs, $"fadetime={MatchDecimalString}");
                    var matchXScale = GetMatchByPattern(matchBackgroundArgs, $"xScale={MatchDecimalString}");
                    var matchYScale = GetMatchByPattern(matchBackgroundArgs, $"yScale={MatchDecimalString}");
                    var matchX = GetMatchByPattern(matchBackgroundArgs, $"x={MatchDecimalString}");
                    var matchY = GetMatchByPattern(matchBackgroundArgs, $"y={MatchDecimalString}");
                    
                    string imageCodeName = matchImageCodename.Groups[1].Value;
                    string screenAdaptMode = matchScreenAdaptMode.Success ? matchScreenAdaptMode.Groups[1].Value : string.Empty;
                    double fadeTime = matchFadeTime.Success ? GetDoubleFromMatch(matchFadeTime) : 0.15d;
                    double xScale = matchXScale.Success ? GetDoubleFromMatch(matchXScale) : 0;
                    double yScale = matchYScale.Success ? GetDoubleFromMatch(matchYScale) : 0;
                    double x = matchX.Success ? GetDoubleFromMatch(matchX) : 0;
                    double y = matchY.Success ? GetDoubleFromMatch(matchY) : 0;

                    ShowBackgroundCommand backgroundCommand = new ShowBackgroundCommand(imageCodeName, screenAdaptMode,
                        fadeTime, xScale, yScale, x, y);
                    return backgroundCommand;
                }
            }
            #endregion
            #region MatchSubtitle
            {
                var matchSubtitle = GetMatchByPattern(strToAnalyse, "\\[Subtitle\\(([\\s\\S]*)\\)\\]");
                if (matchSubtitle.Success)
                {
                    string subtitleArgs = matchSubtitle.Groups[1].Value;
                    var matchText = GetMatchByPattern(subtitleArgs, @"text=""([\s\S]*?)""");
                    var matchX = GetMatchByPattern(subtitleArgs, $"x={MatchDecimalString}");
                    var matchY = GetMatchByPattern(subtitleArgs, $"y={MatchDecimalString}");
                    var matchAlignment = GetMatchByPattern(subtitleArgs, @"alignment=""([\s\S]*?)""");
                    var matchSize = GetMatchByPattern(subtitleArgs, $"size={MatchDecimalString}");
                    var matchDelay = GetMatchByPattern(subtitleArgs, $"delay={MatchDecimalString}");
                    var matchWidth = GetMatchByPattern(subtitleArgs, $"width={MatchDecimalString}");

                    string text = matchText.Groups[1].Value;
                    double x = matchX.Success ? GetDoubleFromMatch(matchX) : 0;
                    double y = matchY.Success ? GetDoubleFromMatch(matchY) : 0;
                    double size = matchSize.Success ? GetDoubleFromMatch(matchSize) : 18;
                    double delay = matchDelay.Success ? GetDoubleFromMatch(matchDelay) : 0;
                    double width = matchWidth.Success ? GetDoubleFromMatch(matchWidth) : 675;
                    string alignment = matchAlignment.Success ? matchAlignment.Groups[1].Value : string.Empty;
                    StoryCommand subtitleCommand = new ShowSubtitleCommand(text, x, y, alignment, size, delay, width);
                    return subtitleCommand;
                }
                else
                {
                    var matchHideSubtitle = GetMatchByPattern(strToAnalyse, "\\[subtitle\\]");
                    if (matchHideSubtitle.Success)
                    {
                        HideSubtitleCommand hideSubtitleCommand = new HideSubtitleCommand();
                        return hideSubtitleCommand;
                    }
                }
            }
            #endregion
            #region MatchPlaySound
            {
                var matchPlaySound = GetMatchByPattern(strToAnalyse, @"\[PlaySound\(([\s\S]*)\)\]");
                if (matchPlaySound.Success)
                {
                    string playSoundArgs = matchPlaySound.Groups[1].Value;
                    var matchKey = GetMatchByPattern(playSoundArgs, "key=\"([\\s\\S]*?)\"");
                    var matchChannel = GetMatchByPattern(playSoundArgs, "channel=\"([\\s\\S]*?)\"");
                    var matchVolume = GetMatchByPattern(playSoundArgs, $"volume={MatchDecimalString}");
                    var matchDelay = GetMatchByPattern(playSoundArgs, $"delay={MatchDecimalString}");
                    var matchBlock = GetMatchByPattern(playSoundArgs, "block=(true|false)");
                    var matchLoop = GetMatchByPattern(playSoundArgs, "loop=(true|false)");

                    string key = matchKey.Groups[1].Value;
                    string channel = matchChannel.Success ? matchChannel.Groups[1].Value : "";
                    double volume = matchVolume.Success ? GetDoubleFromMatch(matchVolume) : 0.5;
                    double delay = matchDelay.Success ? GetDoubleFromMatch(matchDelay) : 0;
                    bool isBlock = matchBlock.Success ? GetBooleanFromMatch(matchBlock) : false;
                    bool isLoop = matchLoop.Success ? GetBooleanFromMatch(matchLoop) : false;
                    PlaySoundCommand playSoundCommand = new PlaySoundCommand(key, channel, volume, delay, isBlock, isLoop); ;
                    return playSoundCommand;
                }
            }
            #endregion
            #region MatchImage
            {
                var matchImage = GetMatchByPattern(strToAnalyse, @"\[image\(([\s\S]*)\)\]");
                if (matchImage.Success)
                {
                    string imageArgs = matchImage.Groups[1].Value;
                    var matchFadeTime = GetMatchByPattern(imageArgs, $"fadetime={MatchDecimalString}");
                    var matchImageCodename = GetMatchByPattern(imageArgs, @"image=""([\s\S]*)""");
                    var matchBlock = GetMatchByPattern(imageArgs, "block=(true|false)");
                    var matchTiled = GetMatchByPattern(imageArgs, "tiled=(true|false)");
                    var matchXScale = GetMatchByPattern(imageArgs, $"xScale={MatchDecimalString}");
                    var matchYScale = GetMatchByPattern(imageArgs, $"yScale={MatchDecimalString}");
                    var matchX = GetMatchByPattern(imageArgs, $"x={MatchDecimalString}");
                    var matchY = GetMatchByPattern(imageArgs, $"y={MatchDecimalString}");
                    var matchScreenAdaptMode = GetMatchByPattern(imageArgs, @"screenadapt=""([\s\S]*?)""");

                    bool isBlock = matchBlock.Success ? GetBooleanFromMatch(matchBlock) : false;
                    bool isTiled = matchTiled.Success ? GetBooleanFromMatch(matchTiled) : false;
                    string imageCodeName = matchImageCodename.Groups[1].Value;
                    string screenAdaptMode = matchScreenAdaptMode.Success ? matchScreenAdaptMode.Groups[1].Value : string.Empty;
                    double fadeTime = matchFadeTime.Success ? GetDoubleFromMatch(matchFadeTime) : 0.15d;
                    double xScale = matchXScale.Success ? GetDoubleFromMatch(matchXScale) : 0;
                    double yScale = matchYScale.Success ? GetDoubleFromMatch(matchYScale) : 0;
                    double x = matchX.Success ? GetDoubleFromMatch(matchX) : 0;
                    double y = matchY.Success ? GetDoubleFromMatch(matchY) : 0;

                    ShowImageCommand showImageCommand = new ShowImageCommand(imageCodeName, screenAdaptMode, fadeTime,
                        isBlock, isTiled, xScale, yScale, x, y);
                    return showImageCommand;
                }
                else
                {
                    var matchHideImage = GetMatchByPattern(strToAnalyse, "\\[image\\]");
                    if (matchHideImage.Success)
                    {
                        HideImageCommand hideImageCommand = new HideImageCommand();
                        return hideImageCommand;
                    }
                }
            }
            #endregion
            #region MatchImageTween
            {
                var matchImageTween = GetMatchByPattern(strToAnalyse, @"\[imageTween\(([\s\S]*)\)\]");
                if (matchImageTween.Success)
                {
                    string imageTweenArgs = matchImageTween.Groups[1].Value;

                    var matchImageCodename = GetMatchByPattern(imageTweenArgs, @"image=""([\s\S]*)""");
                    var matchFadeTime = GetMatchByPattern(imageTweenArgs, $"fadetime={MatchDecimalString}");
                    var matchXScaleFrom = GetMatchByPattern(imageTweenArgs, $"xScaleFrom={MatchDecimalString}");
                    var matchYScaleFrom = GetMatchByPattern(imageTweenArgs, $"yScaleFrom={MatchDecimalString}");
                    var matchXScaleTo = GetMatchByPattern(imageTweenArgs, $"xScaleTo={MatchDecimalString}");
                    var matchYScaleTo = GetMatchByPattern(imageTweenArgs, $"yScaleTo={MatchDecimalString}");
                    var matchXFrom = GetMatchByPattern(imageTweenArgs, $"xFrom={MatchDecimalString}");
                    var matchYFrom = GetMatchByPattern(imageTweenArgs, $"yFrom={MatchDecimalString}");
                    var matchXTo = GetMatchByPattern(imageTweenArgs, $"xTo={MatchDecimalString}");
                    var matchYTo = GetMatchByPattern(imageTweenArgs, $"yTo={MatchDecimalString}");
                    var matchDuration = GetMatchByPattern(imageTweenArgs, $"duration={MatchDecimalString}");
                    var matchBlock = GetMatchByPattern(imageTweenArgs, "block=(true|false)");

                    string imageCodename = matchImageCodename.Success ? matchImageCodename.Groups[1].Value : string.Empty;
                    double fadeTime = matchFadeTime.Success ? GetDoubleFromMatch(matchFadeTime) : 0d;
                    double xScaleFrom = matchXScaleFrom.Success ? GetDoubleFromMatch(matchXScaleFrom) : 0d;
                    double yScaleFrom = matchYScaleFrom.Success ? GetDoubleFromMatch(matchYScaleFrom) : 0d;
                    double xScaleTo = matchXScaleTo.Success ? GetDoubleFromMatch(matchXScaleTo) : 0d;
                    double yScaleTo = matchYScaleTo.Success ? GetDoubleFromMatch(matchYScaleTo) : 0d;
                    double xFrom = matchXFrom.Success ? GetDoubleFromMatch(matchXFrom) : 0d;
                    double yFrom = matchYFrom.Success ? GetDoubleFromMatch(matchYFrom) : 0d;
                    double xTo = matchXTo.Success ? GetDoubleFromMatch(matchXTo) : 0d;
                    double yTo = matchYTo.Success ? GetDoubleFromMatch(matchYTo) : 0d;
                    double duration = matchDuration.Success ? GetDoubleFromMatch(matchDuration) : 0d;
                    bool isBlock = matchBlock.Success ? GetBooleanFromMatch(matchBlock) : false;

                    StartImageTweenCommand imageTweenCommand = new StartImageTweenCommand(imageCodename, isBlock,
                        fadeTime, duration, xScaleFrom, yScaleFrom, xScaleTo, yScaleTo, xFrom, yFrom, xTo, yTo);
                    return imageTweenCommand;
                }
            }
            #endregion
            #region MatchShowItem
            {
                var matchShowItem = GetMatchByPattern(strToAnalyse, @"\[ShowItem\(([\s\S]*)\)\]");
                if (matchShowItem.Success)
                {
                    string showItemArgs = matchShowItem.Groups[1].Value;

                    var matchImageCodename = GetMatchByPattern(showItemArgs, @"image=""([\s\S]*)""");
                    var matchBlock = GetMatchByPattern(showItemArgs, "block=(true|false)");
                    var matchFadeTime = GetMatchByPattern(showItemArgs, $"fadetime={MatchDecimalString}");
                    var matchFadeStyle = GetMatchByPattern(showItemArgs, @"fadestyle=""([\s\S]*)""");
                    var matchOffsetX = GetMatchByPattern(showItemArgs, $"offsetx={MatchDecimalString}");

                    string imageCodename = matchImageCodename.Success ? matchImageCodename.Groups[1].Value : string.Empty;
                    bool isBlock = matchBlock.Success ? GetBooleanFromMatch(matchBlock) : false;
                    double fadeTime = matchFadeTime.Success ? GetDoubleFromMatch(matchFadeTime) : 0d;
                    double offsetX = matchOffsetX.Success ? GetDoubleFromMatch(matchOffsetX) : 0d;
                    string fadeStyle = matchFadeStyle.Success ? matchFadeStyle.Groups[1].Value : string.Empty;

                    ShowItemCommand showItemCommand = new ShowItemCommand(imageCodename, isBlock, fadeTime, fadeStyle, offsetX);
                    return showItemCommand;
                }
            }
            #endregion
            #region MatchHideItem
            {
                var matchHideItem = GetMatchByPattern(strToAnalyse, @"\[hideitem\]");
                if (matchHideItem.Success)
                {
                    HideItemCommand hideItemCommand = new HideItemCommand();
                    return hideItemCommand;
                }
            }
            #endregion
            #region MatchCameraShake
            {
                var matchCameraShake = GetMatchByPattern(strToAnalyse, @"\[CameraShake\(([\s\S]*)\)\]");
                if (matchCameraShake.Success)
                {
                    string cameraShakeArgs = matchCameraShake.Groups[1].Value;

                    var matchDuration = GetMatchByPattern(cameraShakeArgs, $"duration={MatchDecimalString}");
                    var matchXStrength = GetMatchByPattern(cameraShakeArgs, $"xstrength={MatchDecimalString}");
                    var matchYStrength = GetMatchByPattern(cameraShakeArgs, $"ystrength={MatchDecimalString}");
                    var matchVibrato = GetMatchByPattern(cameraShakeArgs, $"vibrato={MatchDecimalString}");
                    var matchRandomness = GetMatchByPattern(cameraShakeArgs, $"randomness={MatchDecimalString}");
                    var matchFadeout = GetMatchByPattern(cameraShakeArgs, "fadeout=(true|false)");
                    var matchBlock = GetMatchByPattern(cameraShakeArgs, "block=(true|false)");
                    var matchStop = GetMatchByPattern(cameraShakeArgs, "stop=(true|false)");

                    double duration = matchDuration.Success ? GetDoubleFromMatch(matchDuration) : -1d;
                    double xStrength = matchXStrength.Success ? GetDoubleFromMatch(matchXStrength) : 0d;
                    double yStrength = matchYStrength.Success ? GetDoubleFromMatch(matchYStrength) : 0d;
                    double vibrato = matchVibrato.Success ? GetDoubleFromMatch(matchVibrato) : 30d;
                    double randomness = matchRandomness.Success ? GetDoubleFromMatch(matchRandomness) : 90d;
                    bool isFadeout = matchFadeout.Success ? GetBooleanFromMatch(matchFadeout) : false;
                    bool isBlock = matchBlock.Success ? GetBooleanFromMatch(matchBlock) : false;
                    bool isStop = matchStop.Success ? GetBooleanFromMatch(matchStop) : false;

                    StartCameraShake cameraShakeCommand = new StartCameraShake(duration, xStrength, yStrength, vibrato, randomness, isFadeout, isBlock, isStop);
                    return cameraShakeCommand;
                }
            }
            #endregion
            #region MatchPopupDialog
            {
                if (convertPopupDialog)
                {
                    var matchPopupDialogText = GetMatchByPattern(strToAnalyse, @"\[PopupDialog\([\s\S]*\)\]\s+([\s\S]*)");
                    if (matchPopupDialogText.Success)
                    {
                        string text = matchPopupDialogText.Groups[1].Value;
                        return new ShowPlainTextCommand(text);
                    }
                }
                else
                {
                    return new NoOperationCommand();
                }
            }
            #endregion
            #region MatchVideo
            {
                var matchVideo = GetMatchByPattern(strToAnalyse, @"\[Video\(res=""([\s\S]*)""\)\]");
                if (matchVideo.Success)
                {
                    string VideoArgs = matchVideo.Groups[1].Value;
                    
                    PlayVideoCommand playVideoCommand = new PlayVideoCommand(VideoArgs);
                    return playVideoCommand;
                }
            }
            #endregion
            throw new ArgumentException($@"无法分析参数""{nameof(strToAnalyse)}"",原始文件行:第{textLine}行,原始文本:""{strToAnalyse}""");
        }

        /// <summary>
        /// 通过要分析的字符串与正则表达式字符串获取<seealso cref="Match"/>对象
        /// </summary>
        /// <param name="strToAnalyse">要分析的字符串</param>
        /// <param name="pattern">正则表达式字符串</param>
        /// <returns></returns>
        private static Match GetMatchByPattern(string strToAnalyse, string pattern)
        {
            return Regex.Match(strToAnalyse, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        /// <summary>
        /// 通过<see cref="Match"/>对象获取其结果字符串表示的<see cref="double"/>实例
        /// </summary>
        /// <param name="match">一个包含表示一个<see cref="double"/>实例的字符串的<see cref="Match"/>对象</param>
        /// <returns>解析出的<see cref="double"/>实例</returns>
        /// <exception cref="ArgumentNullException">当参数为空时抛出</exception>
        private static double GetDoubleFromMatch(Match match)
        {
            if (match is null)
            {
                throw new ArgumentNullException(nameof(match));
            }
            string doubleString = match.Groups[1].Value;
            double doubleValue = double.Parse(doubleString);
            return doubleValue;
        }

        /// <summary>
        /// 通过<see cref="Match"/>对象获取其结果字符串表示的<see cref="bool"/>实例
        /// </summary>
        /// <param name="match">一个包含表示一个<see cref="bool"/>实例的字符串的<see cref="Match"/>对象</param>
        /// <returns>解析出的<see cref="bool"/>实例</returns>
        /// <exception cref="ArgumentNullException">当参数为空时抛出</exception>
        private static bool GetBooleanFromMatch(Match match)
        {
            if (match is null)
            {
                throw new ArgumentNullException(nameof(match));
            }

            string booleanString = match.Groups[1].Value;
            bool boolValue = bool.Parse(booleanString);
            return boolValue;
        }
    }
}
