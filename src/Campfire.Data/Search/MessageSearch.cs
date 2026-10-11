using Campfire.Data.Queries;
using Campfire.Data.Records;
using Campfire.Data.Sqlite;

namespace Campfire.Data.Searching;

// Message.search (reference/app/models/message/searchable.rb) over `user.reachable_messages`, as
// SearchesController#set_messages runs it: the FTS5 `message_search_index` matched against the
// quoted terms in the rooms the user is a member of. A 1000-row probe bounding accessible search hits
// preserves older reachable matches behind large private histories.
public static class MessageSearch
{
    // `last_page_of(100)`
    public const int Limit = 100;

    /// <summary>
    /// Quotes each word, so that FTS5 searches for AND, OR, NOT and NEAR rather than
    /// parsing them as operators.
    /// </summary>
    public static string MatchTerms(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return string.Empty;
        }

        var words = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return string.Join(" ", words.Select(w => $"\"{w.Replace("\"", "\"\"")}\""));
    }

    // `Current.user.reachable_messages.search(query).last_page_of(100)` / `Message.search_reachable`:
    // up to the newest hundred matches, oldest first.
    public static List<Message> Reachable(SqliteSession session, long userId, string query)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(query);
        var matchTerms = MatchTerms(query);
        if (string.IsNullOrEmpty(matchTerms))
        {
            return [];
        }

        var probe = session.Query(
            """
            SELECT m.id, mm.user_id IS NOT NULL FROM message_search_index idx
            JOIN messages m ON m.id = idx.rowid
            LEFT JOIN memberships mm ON mm.room_id = m.room_id AND mm.user_id = @user_id
            WHERE idx.body MATCH @match_terms ORDER BY idx.rowid DESC LIMIT 1000
            """,
            reader => (Id: reader.GetInt64(0), Reachable: reader.GetInt32(1) == 1),
            ("@user_id", userId), ("@match_terms", matchTerms));

        var ids = probe.Where(p => p.Reachable).Select(p => p.Id).Take(Limit).ToList();

        // A scoped fallback preserves older reachable hits behind a large private history.
        if (ids.Count < Limit && probe.Count == 1000)
        {
            var fallback = session.Query(
                $"""
                SELECT {Message.Columns} FROM "messages"
                INNER JOIN "rooms" ON "messages"."room_id" = "rooms"."id"
                INNER JOIN "memberships" ON "rooms"."id" = "memberships"."room_id"
                JOIN message_search_index idx ON messages.id = idx.rowid
                WHERE "memberships"."user_id" = @user_id AND (idx.body MATCH @match_terms)
                ORDER BY idx.rowid DESC LIMIT {Limit}
                """,
                Message.Read,
                ("@user_id", userId), ("@match_terms", matchTerms));
            fallback.Reverse();
            return fallback;
        }

        if (ids.Count == 0)
        {
            return [];
        }

        var (placeholders, listParameters) = Sql.List("id", ids);
        var parameters = listParameters.Concat([("@user_id", (object?)userId)]).ToArray();
        return session.Query(
            $"""
            SELECT {Message.Columns} FROM "messages"
            INNER JOIN "rooms" ON "messages"."room_id" = "rooms"."id"
            INNER JOIN "memberships" ON "rooms"."id" = "memberships"."room_id"
            WHERE "memberships"."user_id" = @user_id AND "messages"."id" IN ({placeholders})
            ORDER BY "messages"."id" ASC
            """,
            Message.Read,
            parameters);
    }
}
