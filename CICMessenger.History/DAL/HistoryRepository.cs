using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using CICMessenger.History.DAL.Entities;

namespace CICMessenger.History.DAL
{
    class HistoryRepository : IDisposable
    {
        HistoryContext context;

        public HistoryRepository(HistoryContext context)
        {
            this.context = context;
        }

        public void AddSessionEvent(string sessionId, DateTime stamp, EventType type, string sender, string senderName, IEnumerable<string> recipients, string data)
        {
            var session = context.Sessions.FirstOrDefault(s => s.Id == sessionId);
            if (session != null)
            {
                session.End = session.End < stamp ? stamp : session.End;
                var evnt = new Event() 
                { 
                    Id = Guid.NewGuid().ToString(), 
                    Type = type, 
                    SenderId = sender.ToString(), 
                    Stamp = stamp, 
                    SenderName = senderName 
                };
                evnt.Data = data;
                evnt.Session = session;
                context.Events.Add(evnt);
                context.SaveChanges();
            }
        }

        /// <summary>
        /// Returns the most recent chat messages exchanged with a contact, across all past
        /// sessions, oldest first — used to repopulate a chat window when it is reopened.
        /// </summary>
        public IEnumerable<Event> GetRecentMessagesWithContact(string contactId, int limit)
        {
            var messages = (from evnt in context.Events.Include(e => e.Session).ThenInclude(s => s.Participants)
                            where (evnt.TypeCode == (int)EventType.Message || evnt.TypeCode == (int)EventType.File)
                                  && evnt.Session.Participants.Any(p => p.ContactId == contactId)
                            orderby evnt.Stamp descending
                            select evnt)
                           .Take(limit)
                           .ToList();

            messages.Reverse();
            return messages;
        }

        /// <summary>
        /// Recent messages for a specific (stable) session id, oldest first — used for
        /// rooms, whose session id is chosen once by the app and stays fixed across
        /// reopenings, so a direct lookup is precise (no participant-set ambiguity).
        /// </summary>
        public IEnumerable<Event> GetRecentMessages(string sessionId, int limit)
        {
            var messages = (from evnt in context.Events
                            where evnt.SessionId == sessionId
                                  && (evnt.TypeCode == (int)EventType.Message || evnt.TypeCode == (int)EventType.File)
                            orderby evnt.Stamp descending
                            select evnt)
                           .Take(limit)
                           .ToList();

            messages.Reverse();
            return messages;
        }

        public IEnumerable<Session> GetSessions(SessionCriteria criteria)
        {
            string text = criteria.Text ?? String.Empty;

            var result = (from session in context.Sessions.Include(s => s.Participants)
                          where (criteria.SessionId == null || session.Id == criteria.SessionId) &&
                              (criteria.From == null || session.Start >= criteria.From.Value) &&
                              (criteria.To == null || session.Start <= criteria.To.Value) &&
                              (text.Length == 0 || session.Events.Any(e=>e.Data.Contains(text))) &&
                              (criteria.Participant == null || session.Participants.Any(p => p.ContactId == criteria.Participant))
                          orderby session.Start
                          select session);

            return result.ToList();

        }

        public Session? GetSession(string sessionId)
        {
            var session = context.Sessions.Include(s => s.Participants)
                                          .Include(s => s.Events)
                                          .FirstOrDefault(s => s.Id == sessionId);
            return session;
        }

        public IEnumerable<StatusUpdate> GetStatusUpdates(StatusCriteria criteria)
        {
            var updates = (from update in context.StatusUpdates
                           where (criteria.From == null || update.Stamp >= criteria.From.Value) &&
                                 (criteria.To == null || update.Stamp <= criteria.To.Value)
                           orderby update.Stamp
                           select update);

            return updates.ToList();
        }

        public void ClearChatHistory(string? sessionId = null)
        {
            DeleteSession(sessionId);

            context.SaveChanges();
        }

        public void ClearStatusHistory()
        {
            DeleteAll(context.StatusUpdates, _ => true);

            context.SaveChanges();
        }

        public void AddSession(Session newSession, IEnumerable<Participant> participants)
        {
            var session = GetSession(newSession.Id);
            if (session == null)
            {
                context.Sessions.Add(newSession);
                session = newSession;
            }
            
            foreach (var participant in participants)
                AddParticipant(session, participant);

            context.SaveChanges();
        }

        public void AddParticipant(string sessionId, Participant participant)
        {
            var session = GetSession(sessionId);
            if (session != null)
                AddParticipant(session, participant);

            context.SaveChanges();
        }

        public void DeleteSessions(IEnumerable<string> sessionIds)
        {
            foreach (string sessionId in sessionIds)
                DeleteSession(sessionId);

            context.SaveChanges();
        }

        /// <summary>Deletes individual chat/file events older than <paramref name="cutoffUtc"/> (auto-delete sweep). Sessions and participants are left intact.</summary>
        public void DeleteEventsOlderThan(DateTime cutoffUtc)
        {
            DeleteAll(context.Events, e => e.Stamp < cutoffUtc);
            context.SaveChanges();
        }

        public void AddStatusUpdate(DateTime stamp, string contactId, string contactName, int status)
        {
            context.StatusUpdates.Add(new StatusUpdate() 
            {
                Id = Guid.NewGuid().ToString(),
                ContactId = contactId, 
                ContactName = contactName, 
                StatusCode = status, 
                Stamp = stamp 
            });
            context.SaveChanges();
        }

        public void Dispose()
        {
            context.Dispose();
        }

        void DeleteSession(string? sessionId)
        {
            DeleteAll(context.Events, e => sessionId == null || e.Session.Id == sessionId);
            DeleteAll(context.Participants, p => sessionId == null || p.Session.Id == sessionId);
            DeleteAll(context.Sessions, s => sessionId == null || s.Id == sessionId);
        }

        void AddParticipant(Session session, Participant participant)
        {
            participant.SessionId = session.Id;
            if (!session.Participants.Any(p => p.ContactId == participant.ContactId))
                context.Participants.Add(participant);
        }

        void DeleteAll<TEntity>(DbSet<TEntity> set, Expression<Func<TEntity, bool>> condition) where TEntity:class
        {
            foreach (var item in set.Where(condition))
                set.Remove(item);
        }
    }
}
