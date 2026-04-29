function queryAll(db, sql, params) {
    const stmt = db.prepare(sql);
    if (params) stmt.bind(params);
    const rows = [];
    while (stmt.step()) rows.push(stmt.getAsObject());
    stmt.free();
    return rows;
}
function queryOne(db, sql, params) {
    const rows = queryAll(db, sql, params);
    return rows.length > 0 ? rows[0] : null;
}
