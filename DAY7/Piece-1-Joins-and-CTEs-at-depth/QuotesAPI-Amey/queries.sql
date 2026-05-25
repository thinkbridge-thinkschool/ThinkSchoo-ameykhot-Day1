-- Day 7: Joins and CTEs at Depth
-- Database: thinkschool-quotesdb-amey (Azure SQL)
-- Schema: Authors(Id, Name)  |  Quotes(Id, AuthorId, Text, CreatedAt)

-- ── Step 8: Create tables and seed data ─────────────────────────────────────

CREATE TABLE Authors (
    Id   INT PRIMARY KEY IDENTITY,
    Name NVARCHAR(100)
);
GO

CREATE TABLE Quotes (
    Id        INT PRIMARY KEY IDENTITY,
    AuthorId  INT FOREIGN KEY REFERENCES Authors(Id),
    Text      NVARCHAR(500),
    CreatedAt DATETIME DEFAULT GETDATE()
);
GO

INSERT INTO Authors VALUES
('Marcus Aurelius'),
('Seneca'),
('Epictetus'),
('Aristotle'),
('Plato'),
('Socrates'),
('Friedrich Nietzsche'),
('Immanuel Kant'),
('René Descartes'),
('Confucius');
GO

INSERT INTO Quotes (AuthorId, Text) VALUES
(1, 'You have power over your mind not outside events'),
(1, 'The impediment to action advances action'),
(1, 'Very little is needed to make a happy life'),
(2, 'Luck is what happens when preparation meets opportunity'),
(2, 'We suffer more in imagination than in reality'),
(2, 'Begin at once to live and count each day as a separate life'),
(3, 'Make the best use of what is in your power'),
(3, 'He is a wise man who does not grieve'),
(4, 'Knowing yourself is the beginning of all wisdom'),
(5, 'At the touch of love everyone becomes a poet'),
(6, 'The only true wisdom is in knowing you know nothing'),
(6, 'Education is the kindling of a flame not the filling of a vessel'),
(7, 'Without music life would be a mistake'),
(7, 'That which does not kill us makes us stronger'),
(7, 'In individuals insanity is rare but in groups it is the rule'),
(8, 'Act only according to that maxim by which you can also will it to be universal'),
(8, 'Science is organized knowledge wisdom is organized life'),
(9, 'I think therefore I am'),
(10, 'It does not matter how slowly you go as long as you do not stop'),
(10, 'Life is really simple but we insist on making it complicated');
GO

-- ── Step 9: CTE query — author stats with most-recent quote ─────────────────

WITH QuoteCounts AS (
    SELECT
        AuthorId,
        COUNT(*) AS QuoteCount
    FROM Quotes
    GROUP BY AuthorId
),
LatestQuotes AS (
    SELECT
        AuthorId,
        MAX(CreatedAt) AS LatestDate,
        MAX(Text)      AS LatestQuote
    FROM Quotes
    GROUP BY AuthorId
)
SELECT TOP 10
    a.Name        AS AuthorName,
    qc.QuoteCount,
    lq.LatestQuote,
    lq.LatestDate
FROM Authors         a
INNER JOIN QuoteCounts qc ON a.Id = qc.AuthorId
INNER JOIN LatestQuotes lq ON a.Id = lq.AuthorId
ORDER BY qc.QuoteCount DESC;
GO
