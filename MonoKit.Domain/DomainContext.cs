// --------------------------------------------------------------------------------------------------------------------
// <copyright file="DomainContext.cs" company="sgmunn">
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

namespace MonoKit.Domain
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using MonoKit.Data;
    
    public abstract class DomainContext : IDomainContext
    {
        private readonly Dictionary<Type, List<Func<IDomainContext, IReadModelBuilder>>> registeredBuilders;

        private readonly Dictionary<Type, Func<IDomainContext, ISnapshotRepository>> registeredSnapshotRepositories;

        public DomainContext()
        {
            this.EventBus = new ObservableNotificationEventBus();
            this.registeredBuilders = new Dictionary<Type, List<Func<IDomainContext, IReadModelBuilder>>>();
            this.registeredSnapshotRepositories = new Dictionary<Type, Func<IDomainContext, ISnapshotRepository>>();
        }

        public DomainContext(IAggregateManifestRepository manifest, IEventStoreRepository eventStore)
        {
            this.EventBus = new ObservableNotificationEventBus();
            this.Manifest = manifest;
            this.EventStore = eventStore;
            
            this.registeredBuilders = new Dictionary<Type, List<Func<IDomainContext, IReadModelBuilder>>>();
            this.registeredSnapshotRepositories = new Dictionary<Type, Func<IDomainContext, ISnapshotRepository>>();
        }

        public DomainContext(IAggregateManifestRepository manifest, IEventStoreRepository eventStore, INotificationEventBus eventBus)
        {
            this.Manifest = manifest;
            this.EventBus = eventBus;
            this.EventStore = eventStore;

            this.registeredBuilders = new Dictionary<Type, List<Func<IDomainContext, IReadModelBuilder>>>();
            this.registeredSnapshotRepositories = new Dictionary<Type, Func<IDomainContext, ISnapshotRepository>>();
        }

        public IAggregateManifestRepository Manifest { get; protected set; }

        public IEventStoreRepository EventStore { get; protected set; }

        public INotificationEventBus EventBus { get; protected set; }

        public IEventSerializer EventSerializer { get; protected set; }

        public virtual IUnitOfWorkScope BeginUnitOfWork()
        {
            return new DefaultScope();
        }

        public ICommandExecutor<T> NewCommandExecutor<T>() where T : class, IAggregateRoot, new()
        {
            return new DomainCommandExecutor<T>(this);
        }

        public virtual IAggregateRepository<T> GetAggregateRepository<T>(INotificationEventBus bus) where T : IAggregateRoot, new()
        {
            if (typeof(T).GetInterfaces().Contains(typeof(IEventSourced)))
            {
                return new EventSourcedAggregateRepository<T>(this.EventSerializer, this.EventStore, this.Manifest, new ReadModelBuildingEventBus<T>(this, bus));
            }

            var repo = new SnapshotAggregateRepository<T>(this.GetSnapshotRepository(typeof(T)), this.Manifest, new ReadModelBuildingEventBus<T>(this, bus));

            if (repo as IObservableRepository != null)
            {
                ((IObservableRepository)repo).Changes.Subscribe(evt => bus.Publish(evt.AsDomainEvent()));
            }

            return repo;
        }

        public IList<IReadModelBuilder> GetReadModelBuilders<T>(INotificationEventBus bus) where T : IAggregateRoot, new()
        {
            var result = new List<IReadModelBuilder>();

            if (this.registeredBuilders.ContainsKey(typeof(T)))
            {
                foreach (var factory in this.registeredBuilders[typeof(T)])
                {
                    var repo = factory(this);

                    if (repo as IObservableRepository != null)
                    {
                        ((IObservableRepository)repo).Changes.Subscribe(evt => bus.Publish(evt.AsDomainEvent()));
                    }

                    result.Add(repo);
                }
            }

            return result;
        }

        public void RegisterSnapshot<T>(Func<IDomainContext, ISnapshotRepository> createSnapshotRepository) where T : IAggregateRoot
        {
            this.registeredSnapshotRepositories[typeof(T)] = createSnapshotRepository;
        }

        public void RegisterBuilder<T>(Func<IDomainContext, IReadModelBuilder> createBuilder) where T : IAggregateRoot
        {
            if (!this.registeredBuilders.ContainsKey(typeof(T)))
            {
                this.registeredBuilders [typeof(T)] = new List<Func<IDomainContext, IReadModelBuilder>>();
            }

            this.registeredBuilders [typeof(T)].Add(createBuilder);
        }
        
        protected virtual ISnapshotRepository GetSnapshotRepository(Type aggregateType)
        {
            if (this.registeredSnapshotRepositories.ContainsKey(aggregateType))
            {
                return this.registeredSnapshotRepositories [aggregateType](this);
            }

            return null;
        }
    }
}

