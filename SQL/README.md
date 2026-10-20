# Database scripts

Every `NN_Name.sql` file here is built into the application, which applies any script not yet
applied each time the site starts (`Services/SchemaScriptRunner.cs`). The table
`SchemaScriptsApplied` records each one; the platform owner sees it under
**System Setup ▸ Database Updates**. Nobody runs these in the site4now SQL console any more.

Rules for a new script:

- Name it with the next number: `22_AddSomething.sql`. Never rename or edit a script once it
  has been deployed - write a new one instead.
- Put each statement after its own numbered comment line: `-- 1. What this does`, `-- 2. ...`.
  Each numbered statement is sent to SQL Server on its own, so no `GO`.
- The whole script runs in one transaction: if any statement fails, none of it is kept, nothing
  later is applied, and the failure shows on the Database Updates page.
- A statement containing `DROP`, `DELETE`, `TRUNCATE`, `UPDATE`, `MERGE` or `EXEC` is refused
  (`ON DELETE CASCADE` on a foreign key is fine). If a script genuinely needs one, after review
  add this line to its header, before statement 1:

      -- @allow-risky-statements

- A statement SQL Server will not run inside a transaction (rare for these scripts) needs
  `-- @no-transaction` in the header. Statements before a failing one are then kept.
- Scripts 01-21 were run by hand before this existed. On a database without
  `SchemaScriptsApplied`, the application recognises them from the tables they created and
  records them as already in place.
