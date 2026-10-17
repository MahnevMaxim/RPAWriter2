using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using RPAWriter.Core.Models;

namespace RPAWriter2.Services
{
    /// <summary>A single chat message persisted in the chat history database.</summary>
    public class ChatHistoryMessage
    {
        public ChatMessageKind Kind { get; set; } = ChatMessageKind.SystemNotice;
        public string Text { get; set; } = "";
        public string Reasoning { get; set; } = "";
        public string Mode { get; set; } = "";
        public string ToolName { get; set; } = "";
    }

    /// <summary>A saved chat conversation with its messages.</summary>
    public class ChatConversation
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public DateTime CreatedAtUtc { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
        public List<ChatHistoryMessage> Messages { get; set; } = new List<ChatHistoryMessage>();

        /// <summary>Text shown in the history dropdown.</summary>
        public string DisplayTitle
        {
            get
            {
                var title = string.IsNullOrWhiteSpace(Title) ? "(empty)" : Title;
                var when = UpdatedAtUtc == default(DateTime)
                    ? ""
                    : "  ·  " + UpdatedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
                return title + when;
            }
        }

        /// <summary>Title for a history card: the first message text, or a placeholder.</summary>
        public string CardTitle => string.IsNullOrWhiteSpace(Title) ? "(empty)" : Title;

        /// <summary>Local date shown on a history card.</summary>
        public string DisplayDate => UpdatedAtUtc == default(DateTime)
            ? ""
            : UpdatedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// SQLite-backed chat history. The database lives at %AppData%\RPAWriter2\chat_history.db.
    /// </summary>
    public static class ChatHistoryStore
    {
        private const int MaxStoredConversations = 50;

        public static string DirectoryPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RPAWriter2");

        public static string DatabasePath => Path.Combine(DirectoryPath, "chat_history.db");

        private static string ConnectionString =>
            "Data Source=" + DatabasePath + ";Version=3;Foreign Keys=True;Default Timeout=5;";

        private const string CreateMessagesSql = @"
CREATE TABLE IF NOT EXISTS messages (
    conversation_id TEXT NOT NULL,
    position INTEGER NOT NULL,
    text TEXT NOT NULL,
    reasoning TEXT NULL,
    mode TEXT NULL,
    kind TEXT NULL,
    tool_name TEXT NULL,
    PRIMARY KEY (conversation_id, position),
    FOREIGN KEY (conversation_id) REFERENCES conversations(id) ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS idx_messages_conversation ON messages(conversation_id, position);";

        /// <summary>Creates the database and schema if needed.</summary>
        public static void Initialize()
        {
            Directory.CreateDirectory(DirectoryPath);

            using (var connection = Open())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
CREATE TABLE IF NOT EXISTS conversations (
    id TEXT PRIMARY KEY,
    title TEXT NOT NULL,
    created_at_utc TEXT NOT NULL,
    updated_at_utc TEXT NOT NULL
);" + CreateMessagesSql;
                command.ExecuteNonQuery();
            }

            // Migrate databases created before the kind/tool_name columns existed, and drop the
            // legacy "role" column (history is reset when the old schema is detected).
            using (var connection = Open())
            {
                EnsureColumn(connection, "messages", "kind", "TEXT NULL");
                EnsureColumn(connection, "messages", "tool_name", "TEXT NULL");

                if (HasColumn(connection, "messages", "role"))
                {
                    using (var drop = connection.CreateCommand())
                    {
                        drop.CommandText = "DROP TABLE messages;";
                        drop.ExecuteNonQuery();
                    }
                    using (var recreate = connection.CreateCommand())
                    {
                        recreate.CommandText = CreateMessagesSql;
                        recreate.ExecuteNonQuery();
                    }
                }
            }
        }
        /// <summary>Returns conversation metadata (newest first), without messages.</summary>
        public static IReadOnlyList<ChatConversation> ListConversations(int limit = MaxStoredConversations)
        {
            var result = new List<ChatConversation>();
            using (var connection = Open())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
SELECT id, title, created_at_utc, updated_at_utc
FROM conversations
ORDER BY updated_at_utc DESC
LIMIT @limit;";
                command.Parameters.AddWithValue("@limit", limit);
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                        result.Add(ReadConversationMeta(reader));
                }
            }
            return result;
        }

        /// <summary>Loads a full conversation (messages ordered by position), or null if not found.</summary>
        public static ChatConversation? LoadConversation(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return null;

            using (var connection = Open())
            {
                var conversation = LoadConversationMeta(connection, id);
                if (conversation == null)
                    return null;

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
SELECT text, reasoning, mode, kind, tool_name
FROM messages
WHERE conversation_id = @id
ORDER BY position;";
                    command.Parameters.AddWithValue("@id", id);
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            conversation.Messages.Add(new ChatHistoryMessage
                            {
                                Kind = ParseKind(reader["kind"] as string ?? ""),
                                Text = reader["text"] as string ?? "",
                                Reasoning = reader["reasoning"] as string ?? "",
                                Mode = reader["mode"] as string ?? "",
                                ToolName = reader["tool_name"] as string ?? ""
                            });
                        }
                    }
                }
                return conversation;
            }
        }
        /// <summary>Inserts or updates a conversation and its messages in a single transaction.</summary>
        public static void SaveConversation(ChatConversation conversation)
        {
            if (conversation == null || string.IsNullOrWhiteSpace(conversation.Id))
                return;
            if (conversation.Messages == null)
                conversation.Messages = new List<ChatHistoryMessage>();

            // Do not keep conversations that never got a user message (e.g. a fresh "New chat").
            if (!HasUserMessage(conversation))
            {
                DeleteConversation(conversation.Id);
                return;
            }

            conversation.UpdatedAtUtc = DateTime.UtcNow;

            using (var connection = Open())
            using (var transaction = connection.BeginTransaction())
            {
                using (var upsert = connection.CreateCommand())
                {
                    upsert.Transaction = transaction;
                    upsert.CommandText = @"
INSERT OR REPLACE INTO conversations (id, title, created_at_utc, updated_at_utc)
VALUES (@id, @title, @created, @updated);";
                    upsert.Parameters.AddWithValue("@id", conversation.Id);
                    upsert.Parameters.AddWithValue("@title", string.IsNullOrWhiteSpace(conversation.Title) ? "New chat" : conversation.Title);
                    upsert.Parameters.AddWithValue("@created", ToTimestamp(conversation.CreatedAtUtc));
                    upsert.Parameters.AddWithValue("@updated", ToTimestamp(conversation.UpdatedAtUtc));
                    upsert.ExecuteNonQuery();
                }

                using (var delete = connection.CreateCommand())
                {
                    delete.Transaction = transaction;
                    delete.CommandText = "DELETE FROM messages WHERE conversation_id = @id;";
                    delete.Parameters.AddWithValue("@id", conversation.Id);
                    delete.ExecuteNonQuery();
                }

                using (var insert = connection.CreateCommand())
                {
                    insert.Transaction = transaction;
                    insert.CommandText = @"
INSERT INTO messages (conversation_id, position, text, reasoning, mode, kind, tool_name)
VALUES (@id, @position, @text, @reasoning, @mode, @kind, @tool_name);";
                    var idParam = insert.Parameters.Add("@id", System.Data.DbType.String);
                    var positionParam = insert.Parameters.Add("@position", System.Data.DbType.Int64);
                    var textParam = insert.Parameters.Add("@text", System.Data.DbType.String);
                    var reasoningParam = insert.Parameters.Add("@reasoning", System.Data.DbType.String);
                    var modeParam = insert.Parameters.Add("@mode", System.Data.DbType.String);
                    var kindParam = insert.Parameters.Add("@kind", System.Data.DbType.String);
                    var toolNameParam = insert.Parameters.Add("@tool_name", System.Data.DbType.String);

                    idParam.Value = conversation.Id;
                    for (var i = 0; i < conversation.Messages.Count; i++)
                    {
                        var message = conversation.Messages[i];
                        positionParam.Value = i;
                        textParam.Value = message.Text;
                        reasoningParam.Value = message.Reasoning;
                        modeParam.Value = message.Mode;
                        kindParam.Value = message.Kind.ToString();
                        toolNameParam.Value = message.ToolName ?? "";
                        insert.ExecuteNonQuery();
                    }
                }

                transaction.Commit();
            }
        }
        /// <summary>Deletes a conversation (messages are removed via ON DELETE CASCADE).</summary>
        public static void DeleteConversation(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return;

            using (var connection = Open())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "DELETE FROM conversations WHERE id = @id;";
                command.Parameters.AddWithValue("@id", id);
                command.ExecuteNonQuery();
            }
        }

        /// <summary>Deletes every stored conversation and all of their messages.</summary>
        public static void DeleteAllConversations()
        {
            using (var connection = Open())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "DELETE FROM messages; DELETE FROM conversations;";
                command.ExecuteNonQuery();
            }
        }

        private static bool HasColumn(SQLiteConnection connection, string table, string column)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "PRAGMA table_info(" + table + ");";
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        if (string.Equals(reader["name"] as string, column, StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                }
            }
            return false;
        }

        private static void EnsureColumn(SQLiteConnection connection, string table, string column, string type)
        {
            if (HasColumn(connection, table, column))
                return;

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "ALTER TABLE " + table + " ADD COLUMN " + column + " " + type + ";";
                command.ExecuteNonQuery();
            }
        }

        private static ChatMessageKind ParseKind(string value)
        {
            if (!string.IsNullOrWhiteSpace(value) && Enum.TryParse(value, true, out ChatMessageKind kind))
                return kind;
            return ChatMessageKind.SystemNotice;
        }

        private static SQLiteConnection Open()
        {
            var connection = new SQLiteConnection(ConnectionString);
            connection.Open();
            return connection;
        }

        private static ChatConversation? LoadConversationMeta(SQLiteConnection connection, string id)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT id, title, created_at_utc, updated_at_utc FROM conversations WHERE id = @id;";
                command.Parameters.AddWithValue("@id", id);
                using (var reader = command.ExecuteReader())
                {
                    return reader.Read() ? ReadConversationMeta(reader) : null;
                }
            }
        }

        private static ChatConversation ReadConversationMeta(SQLiteDataReader reader)
        {
            return new ChatConversation
            {
                Id = reader["id"] as string ?? "",
                Title = reader["title"] as string ?? "",
                CreatedAtUtc = ParseTimestamp(reader["created_at_utc"] as string),
                UpdatedAtUtc = ParseTimestamp(reader["updated_at_utc"] as string)
            };
        }

        private static bool HasUserMessage(ChatConversation conversation)
        {
            foreach (var message in conversation.Messages)
                if (message.Kind == ChatMessageKind.User)
                    return true;
            return false;
        }

        private static string ToTimestamp(DateTime value) =>
            value.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);

        private static DateTime ParseTimestamp(string? value)
        {
            if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var result))
                return result;
            return default(DateTime);
        }
    }
}
