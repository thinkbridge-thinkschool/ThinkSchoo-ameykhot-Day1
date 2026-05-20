import { db } from "./db.js";
import type { Quote } from "./types.js";

export class QuoteRepository {

    getAll(page: number, size: number): Quote[] {
        const offset = (page - 1) * size;

        const stmt = db.prepare(`
            SELECT * FROM quotes
            LIMIT ? OFFSET ?
        `);

        return stmt.all(size, offset) as Quote[];
    }

    getById(id: number): Quote | undefined {
        const stmt = db.prepare(`
            SELECT * FROM quotes WHERE id = ?
        `);

        return stmt.get(id) as Quote | undefined;
    }

    create(quote: Quote): Quote {
        const stmt = db.prepare(`
            INSERT INTO quotes(author, text)
            VALUES (?, ?)
        `);

        const result = stmt.run(
            quote.author,
            quote.text
        );

        return {
            id: Number(result.lastInsertRowid),
            ...quote
        };
    }

    delete(id: number): boolean {
        const stmt = db.prepare(`
            DELETE FROM quotes WHERE id = ?
        `);

        const result = stmt.run(id);

        return result.changes > 0;
    }
}
