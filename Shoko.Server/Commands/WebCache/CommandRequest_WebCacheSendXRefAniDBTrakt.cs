﻿using System;
using System.Xml;
using Shoko.Commons.Queue;
using Shoko.Models.Queue;
using Shoko.Models.Server;
using Shoko.Server.Models;
using Shoko.Server.Providers.Azure;
using Shoko.Server.Repositories;

namespace Shoko.Server.Commands
{
    public class CommandRequest_WebCacheSendXRefAniDBTrakt : CommandRequest
    {
        public virtual int CrossRef_AniDB_TraktID { get; set; }

        public override CommandRequestPriority DefaultPriority => CommandRequestPriority.Priority10;

        public override QueueStateStruct PrettyDescription => new QueueStateStruct
        {
            queueState = QueueStateEnum.WebCacheSendXRefAniDBTrakt,
            extraParams = new[] {CrossRef_AniDB_TraktID.ToString()}
        };

        public CommandRequest_WebCacheSendXRefAniDBTrakt()
        {
        }

        public CommandRequest_WebCacheSendXRefAniDBTrakt(int xrefID)
        {
            CrossRef_AniDB_TraktID = xrefID;
            CommandType = (int) CommandRequestType.WebCache_SendXRefAniDBTrakt;
            Priority = (int) DefaultPriority;

            GenerateCommandID();
        }

        public override void ProcessCommand()
        {
            try
            {
                CrossRef_AniDB_TraktV2 xref = Repo.CrossRef_AniDB_TraktV2.GetByID(CrossRef_AniDB_TraktID);
                if (xref == null) return;

                Trakt_Show tvShow = Repo.Trakt_Show.GetByTraktSlug(xref.TraktID);
                if (tvShow == null) return;

                SVR_AniDB_Anime anime = Repo.AniDB_Anime.GetByAnimeID(xref.AnimeID);
                if (anime == null) return;

                string showName = string.Empty;
                if (tvShow != null) showName = tvShow.Title;

                AzureWebAPI.Send_CrossRefAniDBTrakt(xref, anime.MainTitle);
            }
            catch (Exception ex)
            {
                logger.Error(ex,
                    "Error processing CommandRequest_WebCacheSendXRefAniDBTrakt: {0}" + ex);
            }
        }

        public override void GenerateCommandID()
        {
            CommandID = $"CommandRequest_WebCacheSendXRefAniDBTrakt{CrossRef_AniDB_TraktID}";
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
                CrossRef_AniDB_TraktID =
                    int.Parse(TryGetProperty(docCreator, "CommandRequest_WebCacheSendXRefAniDBTrakt",
                        "CrossRef_AniDB_TraktID"));
            }

            return true;
        }
    }
}