using Campfire.Data.Queries;
using Campfire.Data.Records;
using Campfire.Data.Sqlite;

namespace Campfire.Data.Searching;

// Message.search_reachable (reference/app/models/message/searchable.rb), as
// SearchesController#set_messages runs it: the FTS5 `message_search_index` (kept by
// Lifecycle/MessageSearchIndex from each message's plain text) matched against the query's words,
// each quoted (`match_terms`), in the rooms the user is a member of. A probe reads the newest 1000
// matches off the index and keeps the user's; when it runs out before a page, the scoped search
// (`last_page_of_matches`) finds the older ones. The SQL is what Active Record sends; `last`
// reverses each order to DESC and flips the rows back.
public static class MessageSearch
{
    // `size: 100`
    public const int Limit = 100;
    const string limitText = "100";
    const int probeSize = 1000;

    const string probe = """
        SELECT m.id, mm.user_id IS NOT NULL FROM message_search_index idx
        JOIN messages m ON m.id = idx.rowid
        LEFT JOIN memberships mm ON mm.room_id = m.room_id AND mm.user_id = @user_id
        WHERE idx.body MATCH @query ORDER BY idx.rowid DESC LIMIT 1000
        """;

    const string reachable = $"""SELECT {Message.Columns} FROM "messages" INNER JOIN "rooms" ON "messages"."room_id" = "rooms"."id" INNER JOIN "memberships" ON "rooms"."id" = "memberships"."room_id" join message_search_index idx on messages.id = idx.rowid WHERE "memberships"."user_id" = @user_id AND (idx.body match @query)""";

    // `match_terms(query)`: each word quoted, so FTS5 searches for AND, OR, NOT and NEAR rather
    // than parsing them as operators.
    public static string MatchTerms(string query)
    {
        ArgumentNullException.ThrowIfNull(query);
        return string.Join(' ', query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Select(word => $"\"{word.Replace("\"", "\"\"", StringComparison.Ordinal)}\""));
    }

    // `Message.search_reachable(Current.user, query)`: up to the newest hundred matches, oldest
    // first. `query` is SearchQuery.Searchable's.
    public static List<Message> Reachable(SqliteSession session, long userId, string query)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(query);
        var terms = MatchTerms(query);
        var probed = session.Query(probe, reader => (Id: reader.GetInt64(0), Reachable: reader.GetInt64(1) == 1), ("@user_id", userId), ("@query", terms));
        var ids = probed.Where(row => row.Reachable).Select(row => row.Id).Take(Limit).ToList();

        // A scoped fallback preserves older reachable hits behind a large private history.
        var messages = ids.Count < Limit && probed.Count == probeSize
            ? LastPageOfMatches(session, userId, terms)
            : WithIds(session, userId, terms, ids);
        messages.Reverse();
        return messages;
    }

    // `relation.last_page_of_matches(100)`: `reorder("idx.rowid")`, last hundred.
    static List<Message> LastPageOfMatches(SqliteSession session, long userId, string terms) =>
        session.Query($"{reachable} ORDER BY idx.rowid DESC LIMIT {limitText}", Message.Read, ("@user_id", userId), ("@query", terms));

    // `Page.load(relation.where(id: ids).reorder(:id), :last, 100)`
    static List<Message> WithIds(SqliteSession session, long userId, string terms, List<long> ids)
    {
        if (ids.Count == 0)
        {
            return [];
        }
        var (placeholders, parameters) = Sql.List("id", ids);
        return session.Query($"{reachable} AND \"messages\".\"id\" IN ({placeholders}) ORDER BY \"messages\".\"id\" DESC LIMIT {limitText}", Message.Read,
            [("@user_id", userId), ("@query", terms), .. parameters]);
    }
}
