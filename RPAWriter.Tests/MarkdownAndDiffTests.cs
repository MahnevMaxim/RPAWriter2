using System;
using System.Collections.Generic;
using NUnit.Framework;
using RPAWriter.Core.Models;
using RPAWriter.Core.Services;

namespace RPAWriter.Tests
{
    [TestFixture]
    public class MarkdownAndDiffTests
    {
        [Test]
        public void MarkdownContent_TasksToMarkdown_BuildsTable()
        {
            var spec = new SpecModel
            {
                Name = "Test",
                Tasks = new List<TaskDescriptor>
                {
                    new TaskDescriptor
                    {
                        Action = "open",
                        Target = "site",
                        ActivityType = "OpenBrowser",
                        Keywords = new List<string> { "browser" }
                    }
                }
            };

            var md = MarkdownContent.TasksToMarkdown(spec);

            Assert.That(md, Does.Contain("# Задачи — Test"));
            Assert.That(md, Does.Contain("| # | Действие | Объект | Тип активности | Ключевые слова |"));
            Assert.That(md, Does.Contain("| open | site | OpenBrowser | browser |"));
        }

        [Test]
        public void SpecToMarkdown_IncludesTimestampAndSections()
        {
            var spec = new SpecModel
            {
                Name = "RPA-0001",
                Description = "Opens a site and reads a table.",
                Steps = new List<string> { "Open browser", "Read table" },
                Inputs = new List<string> { "url" },
                Outputs = new List<string> { "table data" }
            };

            var md = MarkdownContent.SpecToMarkdown(spec, new DateTime(2026, 10, 16, 14, 30, 5, DateTimeKind.Utc));

            Assert.That(md, Does.Contain("# RPA-0001 — Техническое задание"));
            Assert.That(md, Does.Contain("Сформировано: 2026-10-16 14:30:05 UTC"));
            Assert.That(md, Does.Contain("## Описание"));
            Assert.That(md, Does.Contain("## Шаги"));
            Assert.That(md, Does.Contain("## Входные данные"));
        }

        [Test]
        public void RobotTasksToMarkdown_ListsSolutionPaths()
        {
            var entries = new List<RobotTaskEntry>
            {
                new RobotTaskEntry
                {
                    Task = new TaskDescriptor { Action = "open", Target = "site", Description = "Open the site", ActivityType = "OpenBrowser" },
                    SolutionPaths = new List<string> { "RPA-0001/Main.xaml", "RPA-0001/RpaActions.cs" }
                }
            };

            var md = MarkdownContent.RobotTasksToMarkdown("RPA-0001", entries, new DateTime(2026, 10, 16, 14, 30, 5, DateTimeKind.Utc));

            Assert.That(md, Does.Contain("# Задачи — RPA-0001"));
            Assert.That(md, Does.Contain("Сформировано: 2026-10-16 14:30:05 UTC"));
            Assert.That(md, Does.Contain("| # | Задача | Описание | Тип активности | Файл(ы) решения |"));
            Assert.That(md, Does.Contain("RPA-0001/Main.xaml, RPA-0001/RpaActions.cs"));
        }

        [Test]
        public void TextDiff_DetectsChanges()
        {
            var oldText = "Line 1\nLine 2\nLine 3";
            var newText = "Line 1\nLine 2 changed\nLine 4";

            var diff = TextDiff.Diff(oldText, newText);

            Assert.That(diff.HasChanges, Is.True);
            Assert.That(diff.AddedCount, Is.GreaterThanOrEqualTo(1));
            Assert.That(diff.RemovedCount, Is.GreaterThanOrEqualTo(1));
            Assert.That(diff.Summary, Does.Contain("Discrepancies"));
        }

        [Test]
        public void TextDiff_NoChanges()
        {
            var diff = TextDiff.Diff("a\nb\nc", "a\nb\nc");

            Assert.That(diff.HasChanges, Is.False);
            Assert.That(diff.Summary, Does.Contain("No discrepancies found"));
        }
    }
}
