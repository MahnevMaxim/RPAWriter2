using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using RPAWriter.Core.Models;
using RPAWriter.Core.Services;
using Newtonsoft.Json;

namespace RPAWriter2.Services
{
    /// <summary>
    /// SQLite-backed chat history. The database lives at %AppData%\RPAWriter2\&lt;version&gt;\chat_history.db.
    /// Models use the IChatHistoryStore seam instead of this static class;
    /// SqliteChatHistoryStore adapts one to the other.
    /// </summary>
    public static class ChatHistoryStore
    {
        private const int MaxStoredConversations = 50;

        public static string DirectoryPath => AppDataPaths.Resolve();

        public static string DatabasePath => Path.Combine(DirectoryPath, "chat_history.db");

        private static string ConnectionString =>
            "Data Source=" + DatabasePath + ";Version=3;Foreign Keys=True;Default Timeout=5;";

        /// <summary>Workspace id used to scope all operations; an empty value is the legacy/global scope.</summary>
        public static string WorkspaceId { get; set; } = "";

        private const string CreateMessagesSql = @"
CREATE TABLE IF NOT EXISTS messages (
    conversation_id TEXT NOT NULL,
    position INTEGER NOT NULL,
    text TEXT NOT NULL,
    reasoning TEXT NULL,
    mode TEXT NULL,
    kind TEXT NULL,
    tool_name TEXT NULL,
    options TEXT NULL,
    checkpoint_hash TEXT NULL,
    timestamp_utc TEXT NULL,
    PRIMARY KEY (conversation_id, position),
    FOREIGN KEY (conversation_id) REFERENCES conversations(id) ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS idx_messages_conversation ON messages(conversation_id, position);";

        /// <summary>
        /// Rolling archive of rows removed from <c>messages</c> by a shrink. Because a save replaces
        /// the stored message list, any rows past the new list length are copied here before being
        /// trimmed, so a truncation can always be recovered. Keyed per (conversation, position,
        /// timestamp) so multiple generations survive.
        /// </summary>
        private const string CreateMessagesBackupSql = @"
CREATE TABLE IF NOT EXISTS messages_backup (
    conversation_id TEXT NOT NULL,
    position INTEGER NOT NULL,
    text TEXT NOT NULL,
    reasoning TEXT NULL,
    mode TEXT NULL,
    kind TEXT NULL,
    tool_name TEXT NULL,
    options TEXT NULL,
    checkpoint_hash TEXT NULL,
    timestamp_utc TEXT NULL,
    backed_up_at_utc TEXT NOT NULL,
    PRIMARY KEY (conversation_id, position, backed_up_at_utc)
);";

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
    updated_at_utc TEXT NOT NULL,
    workspace_id TEXT NOT NULL DEFAULT '',
    provider_transcript TEXT NULL
);
" + CreateMessagesSql + CreateMessagesBackupSql;
                command.ExecuteNonQuery();
            }

            // Migrate databases created before the kind/tool_name columns existed, and drop the
            // legacy "role" column (history is reset when the old schema is detected).
            using (var connection = Open())
            {
                EnsureColumn(connection, "conversations", "workspace_id", "TEXT NOT NULL DEFAULT ''");
                EnsureColumn(connection, "conversations", "is_important", "INTEGER NOT NULL DEFAULT 0");
                EnsureColumn(connection, "conversations", "provider_transcript", "TEXT NULL");
                EnsureColumn(connection, "messages", "kind", "TEXT NULL");
                EnsureColumn(connection, "messages", "tool_name", "TEXT NULL");
                EnsureColumn(connection, "messages", "options", "TEXT NULL");
                EnsureColumn(connection, "messages", "checkpoint_hash", "TEXT NULL");
                EnsureColumn(connection, "messages", "timestamp_utc", "TEXT NULL");
                EnsureColumn(connection, "messages_backup", "timestamp_utc", "TEXT NULL");

                using (var index = connection.CreateCommand())
                {
                    index.CommandText = "CREATE INDEX IF NOT EXISTS idx_conversations_workspace ON conversations(workspace_id);";
                    index.ExecuteNonQuery();
                }

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
SELECT id, title, created_at_utc, updated_at_utc, is_important
FROM conversations
WHERE workspace_id = @workspace OR workspace_id = ''
ORDER BY updated_at_utc DESC
LIMIT @limit;";
                command.Parameters.AddWithValue("@limit", limit);
                command.Parameters.AddWithValue("@workspace", WorkspaceId ?? "");
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

                using (var transcript = connection.CreateCommand())
                {
                    transcript.CommandText = "SELECT provider_transcript FROM conversations WHERE id = @id AND (workspace_id = @workspace OR workspace_id = '');";
                    transcript.Parameters.AddWithValue("@id", id);
                    transcript.Parameters.AddWithValue("@workspace", WorkspaceId ?? "");
                    var value = transcript.ExecuteScalar();
                    conversation.ProviderTranscriptJson = value as string ?? "";
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
SELECT text, reasoning, mode, kind, tool_name, options, checkpoint_hash, timestamp_utc
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
                                ToolName = reader["tool_name"] as string ?? "",
                                Options = DeserializeOptions(reader["options"] as string),
                                CheckpointHash = reader["checkpoint_hash"] as string ?? "",
                                TimestampUtc = ParseTimestamp(reader["timestamp_utc"] as string)
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
INSERT OR REPLACE INTO conversations (id, title, created_at_utc, updated_at_utc, workspace_id, is_important, provider_transcript)
VALUES (@id, @title, @created, @updated, @workspace, @important, @provider_transcript);";
                    upsert.Parameters.AddWithValue("@id", conversation.Id);
                    upsert.Parameters.AddWithValue("@title", string.IsNullOrWhiteSpace(conversation.Title) ? "New chat" : conversation.Title);
                    upsert.Parameters.AddWithValue("@created", ToTimestamp(conversation.CreatedAtUtc));
                    upsert.Parameters.AddWithValue("@updated", ToTimestamp(conversation.UpdatedAtUtc));
                    upsert.Parameters.AddWithValue("@workspace", WorkspaceId ?? "");
                    upsert.Parameters.AddWithValue("@important", conversation.IsImportant ? 1 : 0);
                    upsert.Parameters.AddWithValue("@provider_transcript", conversation.ProviderTranscriptJson ?? "");
                    upsert.ExecuteNonQuery();
                }

                var newCount = conversation.Messages.Count;

                // Archive rows past the new list before trimming them, so a shorter save is always
                // recoverable instead of silently losing the tail.
                ArchiveMessagesTail(connection, transaction, conversation.Id, newCount);

                // Upsert each message by position and trim only positions beyond the new list. This
                // reconciles the stored rows to the snapshot without a full delete-and-reinsert pass,
                // so a re-save of the same conversation can never drop a message that is still there.
                using (var insert = connection.CreateCommand())
                {
                    insert.Transaction = transaction;
                    insert.CommandText = @"
INSERT OR REPLACE INTO messages (conversation_id, position, text, reasoning, mode, kind, tool_name, options, checkpoint_hash, timestamp_utc)
VALUES (@id, @position, @text, @reasoning, @mode, @kind, @tool_name, @options, @checkpoint_hash, @timestamp_utc);";
                    var idParam = insert.Parameters.Add("@id", System.Data.DbType.String);
                    var positionParam = insert.Parameters.Add("@position", System.Data.DbType.Int64);
                    var textParam = insert.Parameters.Add("@text", System.Data.DbType.String);
                    var reasoningParam = insert.Parameters.Add("@reasoning", System.Data.DbType.String);
                    var modeParam = insert.Parameters.Add("@mode", System.Data.DbType.String);
                    var kindParam = insert.Parameters.Add("@kind", System.Data.DbType.String);
                    var toolNameParam = insert.Parameters.Add("@tool_name", System.Data.DbType.String);
                    var optionsParam = insert.Parameters.Add("@options", System.Data.DbType.String);
                    var checkpointHashParam = insert.Parameters.Add("@checkpoint_hash", System.Data.DbType.String);
                    var timestampUtcParam = insert.Parameters.Add("@timestamp_utc", System.Data.DbType.String);

                    idParam.Value = conversation.Id;
                    for (var i = 0; i < newCount; i++)
                    {
                        var message = conversation.Messages[i];
                        positionParam.Value = i;
                        textParam.Value = message.Text;
                        reasoningParam.Value = message.Reasoning;
                        modeParam.Value = message.Mode;
                        kindParam.Value = message.Kind.ToString();
                        toolNameParam.Value = message.ToolName ?? "";
                        optionsParam.Value = SerializeOptions(message.Options);
                        checkpointHashParam.Value = message.CheckpointHash ?? "";
                        timestampUtcParam.Value = ToTimestamp(message.TimestampUtc);
                        insert.ExecuteNonQuery();
                    }
                }

                using (var trim = connection.CreateCommand())
                {
                    trim.Transaction = transaction;
                    trim.CommandText = "DELETE FROM messages WHERE conversation_id = @id AND position >= @count;";
                    trim.Parameters.AddWithValue("@id", conversation.Id);
                    trim.Parameters.AddWithValue("@count", newCount);
                    trim.ExecuteNonQuery();
                }

                transaction.Commit();
            }
        }

        /// <summary>Copies rows at or past <paramref name="newCount"/> into the backup table before they are trimmed.</summary>
        private static void ArchiveMessagesTail(SQLiteConnection connection, SQLiteTransaction transaction, string id, int newCount)
        {
            using (var backup = connection.CreateCommand())
            {
                backup.Transaction = transaction;
                backup.CommandText = @"
INSERT OR REPLACE INTO messages_backup (conversation_id, position, text, reasoning, mode, kind, tool_name, options, checkpoint_hash, timestamp_utc, backed_up_at_utc)
SELECT conversation_id, position, text, reasoning, mode, kind, tool_name, options, checkpoint_hash, timestamp_utc, @now
FROM messages
WHERE conversation_id = @id AND position >= @count;";
                backup.Parameters.AddWithValue("@id", id);
                backup.Parameters.AddWithValue("@count", newCount);
                backup.Parameters.AddWithValue("@now", ToTimestamp(DateTime.UtcNow));
                backup.ExecuteNonQuery();
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
                command.CommandText = "DELETE FROM conversations WHERE id = @id AND (workspace_id = @workspace OR workspace_id = '');";
                command.Parameters.AddWithValue("@id", id);
                command.Parameters.AddWithValue("@workspace", WorkspaceId ?? "");
                command.ExecuteNonQuery();
            }
        }

        /// <summary>Deletes every stored conversation and all of their messages.</summary>
        public static void DeleteAllConversations()
        {
            using (var connection = Open())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
DELETE FROM messages WHERE conversation_id IN (SELECT id FROM conversations WHERE workspace_id = @workspace OR workspace_id = '');
DELETE FROM conversations WHERE workspace_id = @workspace OR workspace_id = '';";
                command.Parameters.AddWithValue("@workspace", WorkspaceId ?? "");
                command.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Sets the important flag on a stored conversation without rewriting its messages or
        /// timestamps. The history list only holds metadata, so the toggle must be a targeted
        /// update instead of a full save (which would drop the conversation for lack of a message).
        /// </summary>
        public static void SetConversationImportant(string id, bool isImportant)
        {
            if (string.IsNullOrWhiteSpace(id))
                return;

            using (var connection = Open())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "UPDATE conversations SET is_important = @important WHERE id = @id AND (workspace_id = @workspace OR workspace_id = '');";
                command.Parameters.AddWithValue("@important", isImportant ? 1 : 0);
                command.Parameters.AddWithValue("@id", id);
                command.Parameters.AddWithValue("@workspace", WorkspaceId ?? "");
                command.ExecuteNonQuery();
            }
        }

        private static string SerializeOptions(IReadOnlyList<string>? options)
        {
            if (options == null || options.Count == 0)
                return "[]";
            return JsonConvert.SerializeObject(options);
        }

        private static List<string> DeserializeOptions(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return new List<string>();

            try
            {
                return JsonConvert.DeserializeObject<List<string>>(json) ?? new List<string>();
            }
            catch
            {
                return new List<string>();
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

        private static bool ReadBool(object value) => value is long number && number != 0;

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
                command.CommandText = "SELECT id, title, created_at_utc, updated_at_utc, is_important FROM conversations WHERE id = @id AND (workspace_id = @workspace OR workspace_id = '');";
                command.Parameters.AddWithValue("@id", id);
                command.Parameters.AddWithValue("@workspace", WorkspaceId ?? "");
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
                UpdatedAtUtc = ParseTimestamp(reader["updated_at_utc"] as string),
                IsImportant = ReadBool(reader["is_important"])
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
