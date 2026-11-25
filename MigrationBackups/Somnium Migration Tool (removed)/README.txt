Somnium Space - Photon Fusion 2.1 Migration Tool
================================================

Updates a world project to Photon Fusion 2.1.3 and Community Modules 4.0.
Worlds built with the old Photon version (2.0) do not sync correctly anymore and must be updated and uploaded again.

How to use
1. Make a FULL COPY of your project folder first (or commit everything if you use version control).
2. Open the tool: menu Somnium SDK > Fusion 2.1 Migration.
3. Follow the steps in order. Nothing is changed without a button and a confirmation.

Safety
- A zip backup is made before every step, in the folder "MigrationBackups" next to your Assets folder.
  The Backups tab of the tool restores any of them.
- The log of everything the tool did is in MigrationBackups/migration_log.txt.
- When you are done and happy with the result, use "Finish: remove the migration tool" in the last step,
  and delete the MigrationBackups folder yourself.

What is inside
- Fusion_2.1_Migration_Guide.md                                   (what changes with Fusion 2.1, and how to migrate by hand;
                                                                   also opened by the "Migration guide" button of the tool)
- Packages/Somnium Photon Fusion 2.1.3.unitypackage               (Photon Fusion 2.1.3 and Photon Realtime 5, no demos, no Photon Voice)
- Packages/CommunityModules V4.0.unitypackage                     (Community Modules 4.0, updated for Fusion 2.1)
- Data/                                                           (list of the original files of every Community Modules release,
                                                                   used to tell your own files from the original ones)

If something goes wrong, keep the log file and ask a developer for help with your world name and the Console errors.
