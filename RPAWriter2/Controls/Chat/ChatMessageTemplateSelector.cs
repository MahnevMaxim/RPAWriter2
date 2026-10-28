using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using RPAWriter.Core.Models;

namespace RPAWriter2.Controls.Chat
{
    /// <summary>
    /// Maps each <see cref="ChatMessageKind"/> to its dedicated message control
    /// (one control per discriminator). Templates are built once and cached per control type.
    /// </summary>
    public sealed class ChatMessageTemplateSelector : DataTemplateSelector
    {
        private static readonly Dictionary<ChatMessageKind, Type> ControlByKind =
            new Dictionary<ChatMessageKind, Type>
            {
                [ChatMessageKind.User] = typeof(UserMessageControl),
                [ChatMessageKind.AgentText] = typeof(AgentTextMessageControl),
                [ChatMessageKind.ResultAnswerPlan] = typeof(ResultAnswerPlanControl),
                [ChatMessageKind.ResultAnswerAct] = typeof(ResultAnswerActControl),
                [ChatMessageKind.ToolCall] = typeof(ToolCallMessageControl),
                [ChatMessageKind.ToolBlocked] = typeof(ToolBlockedMessageControl),
                [ChatMessageKind.Error] = typeof(ErrorMessageControl),
                [ChatMessageKind.SystemNotice] = typeof(SystemNoticeMessageControl),
                [ChatMessageKind.Compaction] = typeof(CompactionMessageControl),
                [ChatMessageKind.ModeSwitch] = typeof(ModeSwitchMessageControl),
                [ChatMessageKind.DiffEdit] = typeof(DiffEditMessageControl),
                [ChatMessageKind.Question] = typeof(QuestionMessageControl),
                [ChatMessageKind.Subagent] = typeof(SubagentMessageControl),
                [ChatMessageKind.Checkpoint] = typeof(CheckpointMessageControl)
            };

        private static readonly Dictionary<Type, DataTemplate> TemplateCache =
            new Dictionary<Type, DataTemplate>();

        public override DataTemplate SelectTemplate(object item, DependencyObject container)
        {
            var kind = (item as ChatMessage)?.Kind ?? ChatMessageKind.SystemNotice;
            var controlType = ControlByKind.TryGetValue(kind, out var type)
                ? type
                : typeof(SystemNoticeMessageControl);

            if (!TemplateCache.TryGetValue(controlType, out var template))
            {
                template = new DataTemplate { VisualTree = new FrameworkElementFactory(controlType) };
                TemplateCache[controlType] = template;
            }

            return template;
        }
    }
}
