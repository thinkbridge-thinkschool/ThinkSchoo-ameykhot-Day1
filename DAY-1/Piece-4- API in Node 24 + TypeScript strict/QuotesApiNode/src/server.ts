import http from "node:http";
import { URL } from "node:url";

import { QuoteRepository } from "./repository.js";
import { logger } from "./logger.js";
import { db } from "./db.js";

const repo = new QuoteRepository();

let activeRequests = 0;
let shuttingDown = false;

const server = http.createServer(async (req, res) => {

    activeRequests++;

    req.on("close", () => {
        activeRequests--;
    });

    try {

        if (shuttingDown) {
            res.statusCode = 503;
            res.end("Server shutting down");
            return;
        }

        logger.info({
            method: req.method,
            url: req.url
        });

        const url = new URL(req.url || "", `http://${req.headers.host}`);

        // GET /api/quotes
        if (
            req.method === "GET" &&
            url.pathname === "/api/quotes"
        ) {

            const page = Number(url.searchParams.get("page") || "1");
            const size = Number(url.searchParams.get("size") || "10");

            const data = repo.getAll(page, size);

            res.setHeader("Content-Type", "application/json");
            res.end(JSON.stringify(data));

            return;
        }

        // GET BY ID
        if (
            req.method === "GET" &&
            url.pathname.startsWith("/api/quotes/")
        ) {

            const id = Number(
                url.pathname.split("/")[3]
            );

            const quote = repo.getById(id);

            if (!quote) {
                res.statusCode = 404;
                res.end(JSON.stringify({
                    title: "Not Found"
                }));
                return;
            }

            res.setHeader("Content-Type", "application/json");
            res.end(JSON.stringify(quote));

            return;
        }

        // POST
        if (
            req.method === "POST" &&
            url.pathname === "/api/quotes"
        ) {

            let body = "";

            for await (const chunk of req) {

                if (req.aborted) {
                    logger.warn("Request aborted");
                    return;
                }

                body += chunk;
            }

            const data = JSON.parse(body);

            if (!data.author || !data.text) {

                res.statusCode = 400;

                res.setHeader(
                    "Content-Type",
                    "application/problem+json"
                );

                res.end(JSON.stringify({
                    title: "Validation Error",
                    errors: {
                        author: ["Author required"],
                        text: ["Text required"]
                    }
                }));

                return;
            }

            const created = repo.create(data);

            res.statusCode = 201;

            res.setHeader("Content-Type", "application/json");

            res.end(JSON.stringify(created));

            return;
        }

        // DELETE
        if (
            req.method === "DELETE" &&
            url.pathname.startsWith("/api/quotes/")
        ) {

            const id = Number(
                url.pathname.split("/")[3]
            );

            const ok = repo.delete(id);

            if (!ok) {
                res.statusCode = 404;
                res.end();
                return;
            }

            res.statusCode = 204;
            res.end();

            return;
        }

        res.statusCode = 404;
        res.end();

    } catch (err) {

        logger.error(err);

        res.statusCode = 500;

        res.setHeader(
            "Content-Type",
            "application/problem+json"
        );

        res.end(JSON.stringify({
            title: "Internal Server Error"
        }));
    }
});

server.listen(3000, () => {
    logger.info("Server running on port 3000");
});

process.on("SIGINT", async () => {

    logger.info("SIGINT received");

    shuttingDown = true;

    server.close(() => {

        logger.info("HTTP server closed");

        db.close();

        logger.info("Database closed");

        process.exit(0);
    });

    const wait = setInterval(() => {

        logger.info({
            activeRequests
        });

        if (activeRequests <= 0) {
            clearInterval(wait);
        }

    }, 500);
});
