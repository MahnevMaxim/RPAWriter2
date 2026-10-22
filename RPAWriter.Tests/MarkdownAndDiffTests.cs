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
            Assert.That(md, Does.Contain("| # | Действие | Объект | Тип активности | Ключевые слова | Транзакция SAP | Вид действия | Оценка (часы) |"));
            Assert.That(md, Does.Contain("| 1 | open | site | OpenBrowser | browser |  |  |  |"));
            Assert.That(md, Does.Contain("**Итого: 0 ч**"));
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
            Assert.That(md, Does.Contain("| # | Задача | Описание | Тип активности | Файл(ы) решения | Транзакция SAP | Вид действия |"));
            Assert.That(md, Does.Contain("RPA-0001/Main.xaml, RPA-0001/RpaActions.cs"));
        }

        [Test]
        public void RobotTasksToMarkdown_RendersSapTransactionColumns()
        {
            var entries = new List<RobotTaskEntry>
            {
                new RobotTaskEntry
                {
                    Task = new TaskDescriptor
                    {
                        Action = "настроить",
                        Target = "VA01",
                        Description = "Настройка и запуск транзакции VA01",
                        ActivityType = "SapSetup",
                        SapTransactionCode = "VA01",
                        SapActionKind = SapActionKind.Setup
                    },
                    SolutionPaths = new List<string> { "RPA-0001/Main.xaml" }
                },
                new RobotTaskEntry
                {
                    Task = new TaskDescriptor
                    {
                        Action = "получить таблицу",
                        Target = "SE16N",
                        Description = "Получение таблицы из транзакции SE16N",
                        ActivityType = "SapGetTable",
                        SapTransactionCode = "SE16N",
                        SapActionKind = SapActionKind.GetTable
                    },
                    SolutionPaths = new List<string> { "RPA-0001/Main.xaml" }
                }
            };

            var md = MarkdownContent.RobotTasksToMarkdown("RPA-0001", entries, new DateTime(2026, 10, 16, 14, 30, 5, DateTimeKind.Utc));

            Assert.That(md, Does.Contain("| # | Задача | Описание | Тип активности | Файл(ы) решения | Транзакция SAP | Вид действия |"));
            Assert.That(md, Does.Contain("| VA01 | настройка |"));
            Assert.That(md, Does.Contain("| SE16N | получение таблицы |"));
        }

        [Test]
        public void TasksToMarkdown_RendersSapTransactionColumns()
        {
            var spec = new SpecModel
            {
                Name = "SAP",
                Tasks = new List<TaskDescriptor>
                {
                    new TaskDescriptor
                    {
                        Action = "настроить",
                        Target = "VA01",
                        ActivityType = "SapSetup",
                        SapTransactionCode = "VA01",
                        SapActionKind = SapActionKind.Setup
                    }
                }
            };

            var md = MarkdownContent.TasksToMarkdown(spec);

            Assert.That(md, Does.Contain("| # | Действие | Объект | Тип активности | Ключевые слова | Транзакция SAP | Вид действия | Оценка (часы) |"));
            Assert.That(md, Does.Contain("| VA01 | настройка |"));
        }

        [Test]
        public void TasksToMarkdown_RendersEstimatedHoursAndTotal()
        {
            var spec = new SpecModel
            {
                Name = "Timed",
                Tasks = new List<TaskDescriptor>
                {
                    new TaskDescriptor { Action = "open", Target = "site", ActivityType = "OpenBrowser", EstimatedHours = 2 },
                    new TaskDescriptor { Action = "fill", Target = "form", ActivityType = "FillField", EstimatedHours = 0.5 },
                    new TaskDescriptor { Action = "save", Target = "file", ActivityType = "WriteExcel" }
                }
            };

            var md = MarkdownContent.TasksToMarkdown(spec);

            Assert.That(md, Does.Contain("| # | Действие | Объект | Тип активности | Ключевые слова | Транзакция SAP | Вид действия | Оценка (часы) |"));
            Assert.That(md, Does.Contain("| 1 | open | site | OpenBrowser |  |  |  | 2 |"));
            Assert.That(md, Does.Contain("| 2 | fill | form | FillField |  |  |  | 0.5 |"));
            Assert.That(md, Does.Contain("| 3 | save | file | WriteExcel |  |  |  |  |"));
            Assert.That(md, Does.Contain("**Итого: 2.5 ч**"));
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
