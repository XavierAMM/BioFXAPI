BACKUP DATABASE BioFXBD
TO DISK = 'C:\Backups\BioFXBD_Backup_01052026.bak'
WITH INIT,
     NAME = 'Full Backup BioFXBD 01/05/2026',
     SKIP,
     STATS = 10;
