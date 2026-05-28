-- ============================================================
-- 01_setup.sql  -  Create tables and seed data
-- Run this ONCE before the repro scripts
-- ============================================================

USE master;
GO

IF DB_ID('DeadlockDemo') IS NOT NULL
    DROP DATABASE DeadlockDemo;
GO

CREATE DATABASE DeadlockDemo;
GO

USE DeadlockDemo;
GO

CREATE TABLE AccountA (
    Id      INT           PRIMARY KEY,
    Balance DECIMAL(10,2) NOT NULL
);

CREATE TABLE AccountB (
    Id      INT           PRIMARY KEY,
    Balance DECIMAL(10,2) NOT NULL
);

INSERT INTO AccountA VALUES (1, 1000.00);
INSERT INTO AccountB VALUES (1, 2000.00);
GO

SELECT 'AccountA' AS [Table], Id, Balance FROM AccountA
UNION ALL
SELECT 'AccountB',             Id, Balance FROM AccountB;
GO
