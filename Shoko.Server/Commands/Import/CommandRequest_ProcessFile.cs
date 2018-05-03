﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Xml;
using AniDBAPI;

using Shoko.Commons.Queue;
using Shoko.Models.Azure;
using Shoko.Models.Queue;
using Shoko.Models.Server;
using Shoko.Server.Models;
using Shoko.Server.Providers.Azure;
using Shoko.Server.Repositories;

namespace Shoko.Server.Commands
{
    [Serializable]
    public class CommandRequest_ProcessFile : CommandRequest
    {
        public virtual int VideoLocalID { get; set; }
        public virtual bool ForceAniDB { get; set; }

        private SVR_VideoLocal vlocal;

        public override CommandRequestPriority DefaultPriority => CommandRequestPriority.Priority3;

        public override QueueStateStruct PrettyDescription
        {
            get
            {
                if (vlocal != null)
                    return new QueueStateStruct
                    {
                        queueState = QueueStateEnum.FileInfo,
                        extraParams = new[] {vlocal.FileName}
                    };
                return new QueueStateStruct
                {
                    queueState = QueueStateEnum.FileInfo,
                    extraParams = new[] {VideoLocalID.ToString()}
                };
            }
        }

        public CommandRequest_ProcessFile()
        {
        }

        public CommandRequest_ProcessFile(int vidLocalID, bool forceAniDB)
        {
            VideoLocalID = vidLocalID;
            ForceAniDB = forceAniDB;
            CommandType = (int) CommandRequestType.ProcessFile;
            Priority = (int) DefaultPriority;

            GenerateCommandID();
        }

        public override void ProcessCommand()
        {
            logger.Trace($"Processing File: {VideoLocalID}");

            Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfo(ServerSettings.Culture);

            try
            {
                if (vlocal == null) vlocal = Repo.VideoLocal.GetByID(VideoLocalID);
                if (vlocal == null) return;

                //now that we have all the has info, we can get the AniDB Info
                ProcessFile_AniDB(vlocal);
            }
            catch (Exception ex)
            {
                logger.Error($"Error processing CommandRequest_ProcessFile: {VideoLocalID} - {ex}");
            }
        }

        private void ProcessFile_AniDB(SVR_VideoLocal vidLocal)
        {
            logger.Trace($"Checking for AniDB_File record for: {vidLocal.Hash} --- {vidLocal.FileName}");
            // check if we already have this AniDB_File info in the database

            lock (vidLocal)
            {
                SVR_AniDB_File aniFile = ForceAniDB ? null : Repo.AniDB_File.GetByHashAndFileSize(vidLocal.Hash, vlocal.FileSize);
                // If cross refs were wiped, but the AniDB_File was not, we unfortunately need to requery the info
                List<CrossRef_File_Episode> crossRefs = Repo.CrossRef_File_Episode.GetByHash(vidLocal.Hash);
                if (crossRefs == null || crossRefs.Count == 0) aniFile = null;

                int animeID = 0;

                if (aniFile == null)
                {
                    // get info from AniDB
                    logger.Debug("Getting AniDB_File record from AniDB....");
                    Raw_AniDB_File fileInfo = ShokoService.AnidbProcessor.GetFileInfo(vidLocal);
                    string localFileName = vidLocal.GetBestVideoLocalPlace()?.FullServerPath;
                    localFileName = !string.IsNullOrEmpty(localFileName) ? Path.GetFileName(localFileName) : vidLocal.FileName;
                    if (fileInfo != null)
                    {
                        using (var upd = Repo.AniDB_File.BeginAddOrUpdate(() => Repo.AniDB_File.GetByHashAndFileSize(vidLocal.Hash, vlocal.FileSize)))
                        {
                            upd.Entity.Populate_RA(fileInfo);
                            upd.Entity.FileName = localFileName;
                            aniFile=upd.Commit();
                        }
                        if (!string.IsNullOrEmpty(fileInfo.OtherEpisodesRAW))
                        {
                            string[] epIDs = fileInfo.OtherEpisodesRAW.Split(',');
                            foreach (string epid in epIDs)
                            {
                                if (!int.TryParse(epid, out int id)) continue;
                                CommandRequest_GetEpisode cmdEp = new CommandRequest_GetEpisode(id);
                                cmdEp.Save();
                            }
                        }
                    }
                    if (aniFile != null)
                    {
                        aniFile.CreateLanguages();
                        aniFile.CreateCrossEpisodes(localFileName);
                        animeID = aniFile.AnimeID;
                    }
                }

                bool missingEpisodes = false;

                // if we still haven't got the AniDB_File Info we try the web cache or local records
                if (aniFile == null)
                {
                    // check if we have any records from previous imports
                    crossRefs = Repo.CrossRef_File_Episode.GetByHash(vidLocal.Hash);
                    if (crossRefs == null || crossRefs.Count == 0)
                    {
                        // lets see if we can find the episode/anime info from the web cache
                        if (ServerSettings.WebCache_XRefFileEpisode_Get)
                        {
                            List<Azure_CrossRef_File_Episode> xrefs =
                                AzureWebAPI.Get_CrossRefFileEpisode(vidLocal);

                            crossRefs = new List<CrossRef_File_Episode>();
                            if (xrefs == null || xrefs.Count == 0)
                            {
                                logger.Debug(
                                    $"Cannot find AniDB_File record or get cross ref from web cache record so exiting: {vidLocal.ED2KHash}");
                                return;
                            }
                            string fileName = vidLocal.GetBestVideoLocalPlace()?.FullServerPath;
                            fileName = !string.IsNullOrEmpty(fileName) ? Path.GetFileName(fileName) : vidLocal.FileName;
                            foreach (Azure_CrossRef_File_Episode xref in xrefs)
                            {
                                CrossRef_File_Episode xrefEnt = new CrossRef_File_Episode
                                {
                                    Hash = vidLocal.ED2KHash,
                                    FileName = fileName,
                                    FileSize = vidLocal.FileSize,
                                    CrossRefSource = (int)CrossRefSource.WebCache,
                                    AnimeID = xref.AnimeID,
                                    EpisodeID = xref.EpisodeID,
                                    Percentage = xref.Percentage,
                                    EpisodeOrder = xref.EpisodeOrder
                                };
                                bool duplicate = false;

                                foreach (CrossRef_File_Episode xrefcheck in crossRefs)
                                {
                                    if (xrefcheck.AnimeID == xrefEnt.AnimeID &&
                                        xrefcheck.EpisodeID == xrefEnt.EpisodeID &&
                                        xrefcheck.Hash == xrefEnt.Hash)
                                        duplicate = true;
                                }

                                if (!duplicate)
                                {
                                    crossRefs.Add(xrefEnt);
                                    Repo.CrossRef_File_Episode.BeginAdd(xrefEnt).Commit();
                                }
                            }
                        }
                        else
                        {
                            logger.Debug($"Cannot get AniDB_File record so exiting: {vidLocal.ED2KHash}");
                            return;
                        }
                    }

                    // we assume that all episodes belong to the same anime
                    foreach (CrossRef_File_Episode xref in crossRefs)
                    {
                        animeID = xref.AnimeID;

                        AniDB_Episode ep = Repo.AniDB_Episode.GetByEpisodeID(xref.EpisodeID);
                        if (ep == null) missingEpisodes = true;
                    }
                }
                else
                {
                    // check if we have the episode info
                    // if we don't, we will need to re-download the anime info (which also has episode info)

                    if (aniFile.EpisodeCrossRefs.Count == 0)
                    {
                        animeID = aniFile.AnimeID;

                        // if we have the anidb file, but no cross refs it means something has been broken
                        logger.Debug($"Could not find any cross ref records for: {vidLocal.ED2KHash}");
                        missingEpisodes = true;
                    }
                    else
                    {
                        foreach (CrossRef_File_Episode xref in aniFile.EpisodeCrossRefs)
                        {
                            AniDB_Episode ep = Repo.AniDB_Episode.GetByEpisodeID(xref.EpisodeID);
                            if (ep == null)
                                missingEpisodes = true;

                            animeID = xref.AnimeID;
                        }
                    }
                }

                // get from DB
                SVR_AniDB_Anime anime = Repo.AniDB_Anime.GetByAnimeID(animeID);
                bool animeRecentlyUpdated = false;

                if (anime != null)
                {
                    TimeSpan ts = DateTime.Now - anime.DateTimeUpdated;
                    if (ts.TotalHours < 4) animeRecentlyUpdated = true;
                }
                else
                    missingEpisodes = true;

                // even if we are missing episode info, don't get data  more than once every 4 hours
                // this is to prevent banning
                if (missingEpisodes && !animeRecentlyUpdated)
                {
                    logger.Debug("Getting Anime record from AniDB....");
                    anime = ShokoService.AnidbProcessor.GetAnimeInfoHTTP(animeID, true,
                        ServerSettings.AutoGroupSeries || ServerSettings.AniDB_DownloadRelatedAnime);
                }

                // create the group/series/episode records if needed
                SVR_AnimeSeries ser = null;
                if (anime != null)
                {
                    logger.Debug("Creating groups, series and episodes....");
                    // check if there is an AnimeSeries Record associated with this AnimeID

                    ser = Repo.AnimeSeries.GetByAnimeID(animeID);
                    AnimeGroup created_grp=null;
                    if (ser == null)
                    {
                        created_grp = anime.CreateAnimeGroup();
                    }
                    DateTime now=DateTime.Now;
                    using (var supd = Repo.AnimeSeries.BeginAddOrUpdate(() => Repo.AnimeSeries.GetByAnimeID(animeID)))
                    {
                        supd.Entity.Populate_RA(anime);
                        if (created_grp != null)
                            supd.Entity.AnimeGroupID = created_grp.AnimeGroupID;
                        supd.Entity.EpisodeAddedDate = now;
                        ser=supd.Commit((false, true, false, false));
                    }
                    ser.CreateAnimeEpisodes();
                    if (created_grp!=null)
                        anime.TriggerAssociations();
                    // check if we have any group status data for this associated anime
                    // if not we will download it now
                    if (Repo.AniDB_GroupStatus.GetByAnimeID(anime.AnimeID).Count == 0)
                    {
                        CommandRequest_GetReleaseGroupStatus cmdStatus =
                            new CommandRequest_GetReleaseGroupStatus(anime.AnimeID, false);
                        cmdStatus.Save();
                    }

                    using (var gupd = Repo.AnimeGroup.BeginBatchUpdate(() => ser.AllGroupsAbove))
                    {
                        foreach (SVR_AnimeGroup grp in gupd)
                        {
                            grp.EpisodeAddedDate = now;
                            gupd.Update(grp);
                        }
                        gupd.Commit((true, false, true));
                    }
                    if (ServerSettings.FileQualityFilterEnabled)
                    {
                        // We do this inside, as the info will not be available as needed otherwise
                        List<SVR_VideoLocal> videoLocals =
                            aniFile?.EpisodeIDs?.SelectMany(a => Repo.VideoLocal.GetByAniDBEpisodeID(a))
                                .Where(b => b != null)
                                .ToList();
                        if (videoLocals != null)
                        {
                            videoLocals.Sort(FileQualityFilter.CompareTo);
                            List<SVR_VideoLocal> keep = videoLocals
                                .Take(FileQualityFilter.Settings.MaxNumberOfFilesToKeep)
                                .ToList();
                            foreach (SVR_VideoLocal vl2 in keep) videoLocals.Remove(vl2);
                            if (!FileQualityFilter.Settings.AllowDeletionOfImportedFiles &&
                                videoLocals.Contains(vidLocal)) videoLocals.Remove(vidLocal);
                            videoLocals = videoLocals.Where(a => !FileQualityFilter.CheckFileKeep(a)).ToList();

                            foreach (SVR_VideoLocal toDelete in videoLocals)
                            {
                                toDelete.Places.ForEach(a => a.RemoveAndDeleteFile());
                            }
                        }
                    }
                }
                else
                {
                    logger.Warn($"Unable to create AniDB_Anime for file: {vidLocal.FileName}");
                }
                vidLocal.Places.ForEach(a => { SVR_VideoLocal_Place.RenameAndMoveAsRequired(a); });

                // update stats for groups and series
                // update all the groups above this series in the heirarchy
                ser?.QueueUpdateStats();


                // Add this file to the users list
                if (ServerSettings.AniDB_MyList_AddFiles)
                {
                    CommandRequest_AddFileToMyList cmd = new CommandRequest_AddFileToMyList(vidLocal.ED2KHash);
                    cmd.Save();
                }
            }
        }

        /// <summary>
        /// This should generate a unique key for a command
        /// It will be used to check whether the command has already been queued before adding it
        /// </summary>
        public override void GenerateCommandID()
        {
            CommandID = $"CommandRequest_ProcessFile_{VideoLocalID}";
        }

        public override bool InitFromDB(Shoko.Models.Server.CommandRequest cq)
        {
            CommandID = cq.CommandID;
            CommandRequestID = cq.CommandRequestID;
            CommandType = cq.CommandType;
            Priority = cq.Priority;
            CommandDetails = cq.CommandDetails;
            DateTimeUpdated = cq.DateTimeUpdated;

            // read xml to get parameters
            if (CommandDetails.Trim().Length > 0)
            {
                XmlDocument docCreator = new XmlDocument();
                docCreator.LoadXml(CommandDetails);

                // populate the fields
                VideoLocalID = int.Parse(TryGetProperty(docCreator, "CommandRequest_ProcessFile", "VideoLocalID"));
                ForceAniDB = bool.Parse(TryGetProperty(docCreator, "CommandRequest_ProcessFile", "ForceAniDB"));
                vlocal = Repo.VideoLocal.GetByID(VideoLocalID);
            }

            return true;
        }
    }
}