-- ====================================================================================
-- Script 01: Database Creation and Collation Configuration
-- Project: Dairy Management System (DMS)
-- Database Name: DairyManagementDb
-- Target RDBMS: Microsoft SQL Server 2019 / 2022
-- Collation: Latin1_General_100_CI_AS_SC_UTF8
-- Author: Senior SQL Server Database Architect & ADO.NET Backend Engineer
-- Description: Creates the DairyManagementDb database if it does not already exist,
--              setting the UTF-8 Unicode collation to ensure linguistic fidelity for
--              multilingual and Gujarati text. Safe and repeatable.
-- ====================================================================================

IF NOT EXISTS (SELECT 1 FROM sys.databases WHERE name = N'DairyManagementDb')
BEGIN
    PRINT 'Creating database DairyManagementDb with collation Latin1_General_100_CI_AS_SC_UTF8...';
    CREATE DATABASE [DairyManagementDb]
    COLLATE Latin1_General_100_CI_AS_SC_UTF8;
END
ELSE
BEGIN
    PRINT 'Database DairyManagementDb already exists.';
END
GO
