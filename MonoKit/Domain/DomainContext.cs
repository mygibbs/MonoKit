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
    using MonoKit.Domain.Data;
    using MonoKit.Data;
    
    public abstract class DomainContext : IDomainContext
    {
        private readonly Dictionary<Type, List<Func<IDomainContext, IDataModelEventBus, IReadModelBuilder>>> registeredBuilders;

        private readonly Dictionary<Type, Func<IDomainContext, ISnapshotRepository>> registeredSnapshotRepositories;

        public DomainContext(IEventStoreRepository eventStore, IDomainEventBus eventBus)
        {
            this.EventBus = eventBus;
            this.EventStore = eventStore;

            this.registeredBuilders = new Dictionary<Type, List<Func<IDomainContext, IDataModelEventBus, IReadModelBuilder>>>();
            this.registeredSnapshotRepositories = new Dictionary<Type, Func<IDomainContext, ISnapshotRepository>>();
        }

        public IEventStoreRepository EventStore{ get; private set; }

        public IDomainEventBus EventBus { get; private set; }

        public IEventSerializer EventSerializer { get; protected set; }

        public virtual IUnitOfWorkScope BeginUnitOfWork()
        {
            return new DefaultScope();
        }

        public ICommandExecutor<T> NewCommandExecutor<T>() where T : class, IAggregateRoot, new()
        {
            return new DomainCommandExecutor<T>(this);
        }

        public virtual IAggregateRepository<T> GetAggregateRepository<T>(IDataModelEventBus bus) where T : IAggregateRoot, new()
        {
            if (typeof(T).GetInterfaces().Contains(typeof(IEventSourced)))
            {
                return new EventSourcedAggregateRepository<T>(this.EventSerializer, this.EventStore, new ReadModelBuildingEventBus<T>(this, bus));
            }

            var repo = new SnapshotAggregateRepository<T>(this.GetSnapshotRepository(typeof(T)), new ReadModelBuildingEventBus<T>(this, bus));

            if (repo as IObservableRepository != null)
            {
                ((IObservableRepository)repo).Changes.Subscribe(evt => bus.Publish(evt));
            }

            return repo;
        }

        public IList<IReadModelBuilder> GetReadModelBuilders<T>(IDataModelEventBus bus) where T : IDataModel, new()
        {
            var result = new List<IReadModelBuilder>();

            if (this.registeredBuilders.ContainsKey(typeof(T)))
            {
                foreach (var factory in this.registeredBuilders[typeof(T)])
                {
                    var repo = factory(this, bus);

                    if (repo as IObservableRepository != null)
                    {
                        ((IObservableRepository)repo).Changes.Subscribe(evt => bus.Publish(evt));
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

        public void RegisterBuilder<T>(Func<IDomainContext, IDataModelEventBus, IReadModelBuilder> createBuilder) where T : IAggregateRoot
        {
            if (!this.registeredBuilders.ContainsKey(typeof(T)))
            {
                this.registeredBuilders [typeof(T)] = new List<Func<IDomainContext, IDataModelEventBus, IReadModelBuilder>>();
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

