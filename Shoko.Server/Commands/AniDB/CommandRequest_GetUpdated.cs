﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Xml;
using Shoko.Commons.Queue;
using Shoko.Server.Repositories.Cached;
using Shoko.Server.Repositories.Direct;
using Shoko.Commons.Utils;
using Shoko.Models.Queue;
using Shoko.Models.Server;
using Shoko.Server.Models;
using Shoko.Server.Repositories;

namespace Shoko.Server.Commands
{
    [Serializable]
    public class CommandRequest_GetUpdated : CommandRequestImplementation, ICommandRequest
    {
        public bool ForceRefresh { get; set; }

        public CommandRequestPriority DefaultPriority
        {
            get { return CommandRequestPriority.Priority4; }
        }

        public QueueStateStruct PrettyDescription
        {
            get
            {
                return new QueueStateStruct()
                {
                    queueState = QueueStateEnum.GetUpdatedAnime,
                    extraParams = new string[0]
                };
            }
        }

        public CommandRequest_GetUpdated()
        {
        }

        public CommandRequest_GetUpdated(bool forced)
        {
            this.ForceRefresh = forced;
            this.CommandType = (int) CommandRequestType.AniDB_GetUpdated;
            this.Priority = (int) DefaultPriority;

            GenerateCommandID();
        }

        public override void ProcessCommand()
        {
            logger.Info("Processing CommandRequest_GetUpdated");

            try
            {
                List<int> animeIDsToUpdate = new List<int>();

                // check the automated update table to see when the last time we ran this command
                ScheduledUpdate sched =
                    RepoFactory.ScheduledUpdate.GetByUpdateType((int) ScheduledUpdateType.AniDBUpdates);
                if (sched != null)
                {
                    int freqHours = Utils.GetScheduledHours(ServerSettings.AniDB_Anime_UpdateFrequency);

                    // if we have run this in the last 12 hours and are not forcing it, then exit
                    TimeSpan tsLastRun = DateTime.Now - sched.LastUpdate;
                    if (tsLastRun.TotalHours < freqHours)
                    {
                        if (!ForceRefresh) return;
                    }
                }


                long webUpdateTime = 0;
                long webUpdateTimeNew = 0;
                if (sched == null)
                {
                    // if this is the first time, lets ask for last 3 days
                    DateTime localTime = DateTime.Now.AddDays(-3);
                    DateTime utcTime = localTime.ToUniversalTime();
                    webUpdateTime = long.Parse(Commons.Utils.AniDB.AniDBDate(utcTime));
                    webUpdateTimeNew = long.Parse(Commons.Utils.AniDB.AniDBDate(DateTime.Now.ToUniversalTime()));

                    sched = new ScheduledUpdate
                    {
                        UpdateType = (int)ScheduledUpdateType.AniDBUpdates
                    };
                }
                else
                {
                    logger.Trace("Last anidb info update was : {0}", sched.UpdateDetails);
                    webUpdateTime = long.Parse(sched.UpdateDetails);
                    webUpdateTimeNew = long.Parse(Commons.Utils.AniDB.AniDBDate(DateTime.Now.ToUniversalTime()));

                    DateTime timeNow = DateTime.Now.ToUniversalTime();
                    logger.Info(string.Format("{0} since last UPDATED command",
                        Utils.FormatSecondsToDisplayTime(int.Parse((webUpdateTimeNew - webUpdateTime).ToString()))));
                }

                // get a list of updates from AniDB
                // startTime will contain the date/time from which the updates apply to
                ShokoService.AnidbProcessor.GetUpdated(ref animeIDsToUpdate, ref webUpdateTime);

                // now save the update time from AniDB
                // we will use this next time as a starting point when querying the web cache
                sched.LastUpdate = DateTime.Now;
                sched.UpdateDetails = webUpdateTimeNew.ToString();
                RepoFactory.ScheduledUpdate.Save(sched);

                if (animeIDsToUpdate.Count == 0)
                {
                    logger.Info("No anime to be updated");
                    return;
                }


                int countAnime = 0;
                int countSeries = 0;
                foreach (int animeID in animeIDsToUpdate)
                {
                    // update the anime from HTTP
                    SVR_AniDB_Anime anime = RepoFactory.AniDB_Anime.GetByAnimeID(animeID);
                    if (anime == null)
                    {
                        logger.Trace("No local record found for Anime ID: {0}, so skipping...", animeID);
                        continue;
                    }

                    logger.Info("Updating CommandRequest_GetUpdated: {0} ", animeID);

                    // but only if it hasn't been recently updated
                    TimeSpan ts = DateTime.Now - anime.DateTimeUpdated;
                    if (ts.TotalHours > 4)
                    {
                        CommandRequest_GetAnimeHTTP cmdAnime = new CommandRequest_GetAnimeHTTP(animeID, true, false);
                        cmdAnime.Save();
                        countAnime++;
                    }

                    // update the group status
                    // this will allow us to determine which anime has missing episodes
                    // so we wonly get by an amime where we also have an associated series
                    SVR_AnimeSeries ser = RepoFactory.AnimeSeries.GetByAnimeID(animeID);
                    if (ser != null)
                    {
                        CommandRequest_GetReleaseGroupStatus cmdStatus =
                            new CommandRequest_GetReleaseGroupStatus(animeID, true);
                        cmdStatus.Save();
                        countSeries++;
                    }
                }

                logger.Info("Updating {0} anime records, and {1} group status records", countAnime, countSeries);
            }
            catch (Exception ex)
            {
                logger.Error("Error processing CommandRequest_GetUpdated: {0}", ex.ToString());
                return;
            }
        }

        public override void GenerateCommandID()
        {
            this.CommandID = string.Format("CommandRequest_GetUpdated");
        }

        public override bool LoadFromDBCommand(CommandRequest cq)
        {
            this.CommandID = cq.CommandID;
            this.CommandRequestID = cq.CommandRequestID;
            this.CommandType = cq.CommandType;
            this.Priority = cq.Priority;
            this.CommandDetails = cq.CommandDetails;
            this.DateTimeUpdated = cq.DateTimeUpdated;

            // read xml to get parameters
            if (this.CommandDetails.Trim().Length > 0)
            {
                XmlDocument docCreator = new XmlDocument();
                docCreator.LoadXml(this.CommandDetails);

                // populate the fields
                this.ForceRefresh = bool.Parse(TryGetProperty(docCreator, "CommandRequest_GetUpdated", "ForceRefresh"));
            }

            return true;
        }

        public override CommandRequest ToDatabaseObject()
        {
            GenerateCommandID();

            CommandRequest cq = new CommandRequest
            {
                CommandID = this.CommandID,
                CommandType = this.CommandType,
                Priority = this.Priority,
                CommandDetails = this.ToXML(),
                DateTimeUpdated = DateTime.Now
            };
            return cq;
        }
    }
}