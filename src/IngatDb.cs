using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace Deskmate
{
    // Thin wrapper over Windows' own SQLite (winsqlite3.dll); one connection, one thread at a time
    sealed class IngatDb : IDisposable
    {
        [DllImport("winsqlite3.dll")] static extern int sqlite3_open_v2(byte[] filename, out IntPtr db, int flags, IntPtr vfs);
        [DllImport("winsqlite3.dll")] static extern int sqlite3_close_v2(IntPtr db);
        [DllImport("winsqlite3.dll")] static extern int sqlite3_busy_timeout(IntPtr db, int ms);
        [DllImport("winsqlite3.dll")] static extern IntPtr sqlite3_errmsg16(IntPtr db);
        [DllImport("winsqlite3.dll", CharSet = CharSet.Unicode)] static extern int sqlite3_prepare16_v2(IntPtr db, string sql, int bytes, out IntPtr stmt, IntPtr tail);
        [DllImport("winsqlite3.dll")] static extern int sqlite3_step(IntPtr stmt);
        [DllImport("winsqlite3.dll")] static extern int sqlite3_reset(IntPtr stmt);
        [DllImport("winsqlite3.dll")] static extern int sqlite3_clear_bindings(IntPtr stmt);
        [DllImport("winsqlite3.dll")] static extern int sqlite3_finalize(IntPtr stmt);
        [DllImport("winsqlite3.dll")] static extern int sqlite3_bind_int64(IntPtr stmt, int i, long v);
        [DllImport("winsqlite3.dll")] static extern int sqlite3_bind_double(IntPtr stmt, int i, double v);
        [DllImport("winsqlite3.dll")] static extern int sqlite3_bind_null(IntPtr stmt, int i);
        [DllImport("winsqlite3.dll", CharSet = CharSet.Unicode)] static extern int sqlite3_bind_text16(IntPtr stmt, int i, string v, int bytes, IntPtr destructor);
        [DllImport("winsqlite3.dll")] static extern int sqlite3_bind_blob(IntPtr stmt, int i, byte[] v, int bytes, IntPtr destructor);
        [DllImport("winsqlite3.dll")] static extern int sqlite3_column_count(IntPtr stmt);
        [DllImport("winsqlite3.dll")] static extern int sqlite3_column_type(IntPtr stmt, int col);
        [DllImport("winsqlite3.dll")] static extern long sqlite3_column_int64(IntPtr stmt, int col);
        [DllImport("winsqlite3.dll")] static extern double sqlite3_column_double(IntPtr stmt, int col);
        [DllImport("winsqlite3.dll")] static extern IntPtr sqlite3_column_text16(IntPtr stmt, int col);
        [DllImport("winsqlite3.dll")] static extern int sqlite3_column_bytes16(IntPtr stmt, int col);
        [DllImport("winsqlite3.dll")] static extern IntPtr sqlite3_column_blob(IntPtr stmt, int col);
        [DllImport("winsqlite3.dll")] static extern int sqlite3_column_bytes(IntPtr stmt, int col);
        [DllImport("winsqlite3.dll")] static extern long sqlite3_last_insert_rowid(IntPtr db);
        [DllImport("winsqlite3.dll")] static extern int sqlite3_changes(IntPtr db);

        // SQLITE_TRANSIENT: SQLite copies the value before the call returns
        static readonly IntPtr Transient = new IntPtr(-1);
        const int Row = 100, Done = 101;
        const int MaxCachedStmts = 96;

        struct CachedStmt { public IntPtr Stmt; public long Used; }

        IntPtr db;
        readonly object gate = new object();
        readonly Dictionary<string, CachedStmt> stmtCache = new Dictionary<string, CachedStmt>(StringComparer.Ordinal);
        long stmtClock;

        public IngatDb(string path)
        {
            // READWRITE | CREATE | FULLMUTEX
            int rc = sqlite3_open_v2(Encoding.UTF8.GetBytes(path + "\0"), out db, 0x2 | 0x4 | 0x10000, IntPtr.Zero);
            if (rc != 0)
            {
                string msg = db != IntPtr.Zero ? Error() : "open " + rc;
                if (db != IntPtr.Zero) sqlite3_close_v2(db);
                db = IntPtr.Zero;
                throw new InvalidOperationException(msg);
            }
            sqlite3_busy_timeout(db, 5000);
        }

        public void ClearStatements()
        {
            lock (gate)
            {
                foreach (var kv in stmtCache) sqlite3_finalize(kv.Value.Stmt);
                stmtCache.Clear();
            }
        }

        static bool Cacheable(string sql)
        {
            if (string.IsNullOrEmpty(sql) || sql.Length > 700) return false;
            int i = 0;
            while (i < sql.Length && char.IsWhiteSpace(sql[i])) i++;
            int rem = sql.Length - i;
            if (rem < 5) return false;
            return StartsWithCi(sql, i, "SELECT ") || StartsWithCi(sql, i, "INSERT ") || StartsWithCi(sql, i, "UPDATE ") ||
                   StartsWithCi(sql, i, "DELETE ") || StartsWithCi(sql, i, "BEGIN") || StartsWithCi(sql, i, "COMMIT") ||
                   StartsWithCi(sql, i, "ROLLBACK");
        }

        static bool StartsWithCi(string s, int offset, string prefix)
        {
            if (s.Length - offset < prefix.Length) return false;
            for (int i = 0; i < prefix.Length; i++)
                if (char.ToUpperInvariant(s[offset + i]) != prefix[i]) return false;
            return true;
        }

        public object Gate { get { return gate; } }

        int txDepth;

        // Runs work as one transaction under the gate: one WAL commit instead of one per statement.
        // Nested calls join the outer transaction; an exception rolls the whole thing back.
        public void Tx(Action work)
        {
            lock (gate)
            {
                if (txDepth > 0) { txDepth++; try { work(); } finally { txDepth--; } return; }
                Exec("BEGIN IMMEDIATE");
                txDepth = 1;
                bool ok = false;
                try { work(); ok = true; }
                finally
                {
                    txDepth = 0;
                    try { Exec(ok ? "COMMIT" : "ROLLBACK"); }
                    catch (InvalidOperationException)
                    {
                        // A failed COMMIT leaves the transaction open: close it so the connection stays usable
                        if (ok) { try { Exec("ROLLBACK"); } catch (InvalidOperationException) { } throw; }
                    }
                }
            }
        }

        string Error()
        {
            var p = sqlite3_errmsg16(db);
            return p == IntPtr.Zero ? "sqlite" : Marshal.PtrToStringUni(p);
        }


        // Statements run one by one; ';' inside strings is not used by our schema
        public void Script(string sql)
        {
            foreach (var part in sql.Split(';'))
                if (part.Trim().Length > 0) Exec(part);
        }

        public int Exec(string sql, params object[] args)
        {
            lock (gate)
            {
                bool cacheable;
                IntPtr st = Acquire(sql, args, out cacheable);
                bool ok = false;
                try
                {
                    int rc;
                    while ((rc = sqlite3_step(st)) == Row) { }
                    if (rc != Done) throw new InvalidOperationException(Error() + " :: " + Head(sql));
                    ok = true;
                    return sqlite3_changes(db);
                }
                finally { Release(sql, st, cacheable && ok); }
            }
        }

        // Runs an INSERT and returns its rowid under the same lock: the rowid is per connection, so another thread
        // inserting between the statement and a separate read would hand back its row instead
        public long Insert(string sql, params object[] args)
        {
            lock (gate)
            {
                Exec(sql, args);
                return sqlite3_last_insert_rowid(db);
            }
        }

        public List<object[]> Query(string sql, params object[] args)
        {
            return QueryCore(sql, args, true);
        }

        // Bypasses the prepared statement cache for one-off dynamic SQL (e.g. IN (...) lists) so hot statements stay cached
        public List<object[]> QueryDirect(string sql, params object[] args)
        {
            return QueryCore(sql, args, false);
        }

        List<object[]> QueryCore(string sql, object[] args, bool allowCache)
        {
            var rows = new List<object[]>();
            lock (gate)
            {
                bool cacheable;
                IntPtr st = Acquire(sql, args, allowCache, out cacheable);
                bool ok = false;
                try
                {
                    int n = sqlite3_column_count(st), rc;
                    while ((rc = sqlite3_step(st)) == Row)
                    {
                        var r = new object[n];
                        for (int i = 0; i < n; i++) r[i] = Column(st, i);
                        rows.Add(r);
                    }
                    if (rc != Done) throw new InvalidOperationException(Error() + " :: " + Head(sql));
                    ok = true;
                }
                finally { Release(sql, st, cacheable && ok); }
            }
            return rows;
        }

        public object Scalar(string sql, params object[] args)
        {
            lock (gate)
            {
                bool cacheable;
                IntPtr st = Acquire(sql, args, out cacheable);
                bool ok = false;
                try
                {
                    int rc = sqlite3_step(st);
                    if (rc == Row)
                    {
                        object v = sqlite3_column_count(st) > 0 ? Column(st, 0) : null;
                        ok = true;
                        return v;
                    }
                    if (rc != Done) throw new InvalidOperationException(Error() + " :: " + Head(sql));
                    ok = true;
                    return null;
                }
                finally { Release(sql, st, cacheable && ok); }
            }
        }

        public long Long(string sql, params object[] args)
        {
            var v = Scalar(sql, args);
            return v is long ? (long)v : v is double ? (long)(double)v : 0;
        }

        static string Head(string sql) { sql = sql.Trim(); return sql.Length > 80 ? sql.Substring(0, 80) : sql; }

        IntPtr Acquire(string sql, object[] args, out bool cacheable)
        {
            return Acquire(sql, args, true, out cacheable);
        }

        IntPtr Acquire(string sql, object[] args, bool allowCache, out bool cacheable)
        {
            cacheable = allowCache && Cacheable(sql);
            IntPtr st;
            CachedStmt entry;
            if (cacheable && stmtCache.TryGetValue(sql, out entry))
            {
                stmtCache.Remove(sql);
                st = entry.Stmt;
            }
            else
            {
                if (sqlite3_prepare16_v2(db, sql, sql.Length * 2, out st, IntPtr.Zero) != 0)
                    throw new InvalidOperationException(Error() + " :: " + Head(sql));
            }
            try
            {
                for (int i = 0; args != null && i < args.Length; i++)
                {
                    var a = args[i];
                    int idx = i + 1;
                    if (a == null) sqlite3_bind_null(st, idx);
                    else if (a is string) { var s = (string)a; sqlite3_bind_text16(st, idx, s, s.Length * 2, Transient); }
                    else if (a is byte[]) { var b = (byte[])a; sqlite3_bind_blob(st, idx, b, b.Length, Transient); }
                    else if (a is double || a is float) sqlite3_bind_double(st, idx, Convert.ToDouble(a));
                    else if (a is bool) sqlite3_bind_int64(st, idx, (bool)a ? 1 : 0);
                    else if (a is DateTime) sqlite3_bind_int64(st, idx, Unix((DateTime)a));
                    else sqlite3_bind_int64(st, idx, Convert.ToInt64(a));
                }
            }
            catch
            {
                sqlite3_finalize(st);
                throw;
            }
            return st;
        }

        void Release(string sql, IntPtr st, bool keep)
        {
            if (!keep || db == IntPtr.Zero) { sqlite3_finalize(st); return; }
            if (sqlite3_reset(st) != 0 || sqlite3_clear_bindings(st) != 0) { sqlite3_finalize(st); return; }
            if (stmtCache.ContainsKey(sql)) { sqlite3_finalize(st); return; }
            if (stmtCache.Count >= MaxCachedStmts)
            {
                string lruKey = null;
                long lruUsed = long.MaxValue;
                foreach (var kv in stmtCache)
                {
                    if (kv.Value.Used < lruUsed) { lruUsed = kv.Value.Used; lruKey = kv.Key; }
                }
                if (lruKey != null)
                {
                    sqlite3_finalize(stmtCache[lruKey].Stmt);
                    stmtCache.Remove(lruKey);
                }
            }
            stmtCache[sql] = new CachedStmt { Stmt = st, Used = ++stmtClock };
        }

        static object Column(IntPtr st, int i)
        {
            switch (sqlite3_column_type(st, i))
            {
                case 1: return sqlite3_column_int64(st, i);
                case 2: return sqlite3_column_double(st, i);
                case 3:
                    var p = sqlite3_column_text16(st, i);
                    return p == IntPtr.Zero ? "" : Marshal.PtrToStringUni(p, sqlite3_column_bytes16(st, i) / 2);
                case 4:
                    int n = sqlite3_column_bytes(st, i);
                    var b = new byte[n];
                    if (n > 0) Marshal.Copy(sqlite3_column_blob(st, i), b, 0, n);
                    return b;
                default: return null;
            }
        }

        // Seconds since 1970, UTC; every time column uses this
        public static long Unix(DateTime t)
        {
            if (t.Kind == DateTimeKind.Local) t = t.ToUniversalTime();
            return (long)(t - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
        }

        public static DateTime FromUnix(long s) { return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(s); }

        public static string Str(object o) { return o as string ?? (o == null ? "" : Convert.ToString(o, System.Globalization.CultureInfo.InvariantCulture)); }
        public static long Num(object o) { return o is long ? (long)o : o is double ? (long)(double)o : 0; }

        public void Dispose()
        {
            lock (gate)
            {
                ClearStatements();
                if (db != IntPtr.Zero) { sqlite3_close_v2(db); db = IntPtr.Zero; }
            }
        }
    }
}
