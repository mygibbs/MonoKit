// --------------------------------------------------------------------------------------------------------------------
// <copyright file="EventSourcedAggregateRepository_T.cs" company="sgmunn">
//   (c) sgmunn 2012  
//
//   Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated 
//   documentation files (the "Software"), to deal in the Software without restriction, including without limitation 
//   the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and 
//   to permit persons to whom the Software is furnished to do so, subject to the following conditions:
//
//   The above copyright notice and this permission notice shall be included in all copies or substantial portions of 
//   the Software.
// 
//   THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO 
//   THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE 
//   AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF 
//   CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS 
//   IN THE SOFTWARE.
// </copyright>
// --------------------------------------------------------------------------------------------------------------------

namespace MonoKit.Domain.Data
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using MonoKit.Data;
    
    // todo: pass in a repository of state and handle snaspshots as well as event sourced aggregates
    public class EventSourcedAggregateRepository<T> : IAggregateRepository<T> where T : IAggregateRoot, new()
    {
        private readonly IEventStoreRepository repository;

        private readonly IEventSerializer serializer;

        private readonly IAggregateManifestRepository manifest;
        
        private readonly IDataModelEventBus eventBus;
  
        public EventSourcedAggregateRepository(IEventSerializer serializer, IEventStoreRepository repository, IAggregateManifestRepository manifest, IDataModelEventBus eventBus)
        {
            this.serializer = serializer;
            this.repository = repository;
            this.eventBus = eventBus;
            this.manifest = manifest;
        }

        public T New()
        {
            return new T();
        }

        public T GetById(IUniqueIdentity id)
        {
            var allEvents = this.repository.GetAllAggregateEvents(id).ToList();

            if (allEvents.Count == 0)
            {
                return default(T);
            }

            var history = new List<IAggregateEvent>();

            foreach (var storedEvent in allEvents)
            {
                var evt = this.serializer.DeserializeFromString(storedEvent.Event);

                // should change id of event to match so that new events from subsequent commands are assigned the correct id
                // this handles serializers that don't restore identity to correct type
                ((IAggregateEvent)evt).Identity = id;

                history.Add((IAggregateEvent)evt);
            }

            var result = this.New();

            ((IEventSourced)result).LoadFromEvents(history);

            return result;
        }

        public IEnumerable<T> GetAll()
        {
            throw new NotSupportedException();
        }

        public void Save(T instance)
        {
            if (!instance.UncommittedEvents.Any())
            {
                return;
            }

            int expectedVersion = instance.UncommittedEvents.First().Version - 1;

            var allEvents = this.repository.GetAllAggregateEvents(instance.Identity).ToList();

            var lastEvent = allEvents.LastOrDefault();
            if ((lastEvent == null && expectedVersion != 0) || (lastEvent != null && lastEvent.Version != expectedVersion))
            {
                throw new ConcurrencyException();
            }

            this.manifest.UpdateManifest(instance.Identity, expectedVersion, instance.UncommittedEvents.Last().Version);

            foreach (var evt in instance.UncommittedEvents.ToList())
            {
                var storedEvent = this.repository.New();
                storedEvent.AggregateId = instance.Identity.Id;
                storedEvent.EventId = evt.EventId;
                storedEvent.Version = evt.Version;
                storedEvent.Event = this.serializer.SerializeToString(evt);

                this.repository.Save(storedEvent);
            }
   
            this.Publish(instance);
            instance.Commit();
        }

        public void Delete(T instance)
        {
            throw new NotSupportedException();
        }

        public void DeleteId(IUniqueIdentity id)
        {
            throw new NotSupportedException();
        }

        public void Dispose()
        {
            this.repository.Dispose();
        }

        private void Publish(IAggregateRoot instance)
        {
            if (this.eventBus != null)
            {
                foreach (var evt in instance.UncommittedEvents.ToList())
                {
                    this.eventBus.Publish(evt);
                }
            }

        }
    }
}

