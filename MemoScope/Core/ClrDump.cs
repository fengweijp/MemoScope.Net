using System.Collections.Generic;
using System.Linq;
using MemoScope.Core.Dac;
using Microsoft.Diagnostics.Runtime;
using System;
using WinFwk.UITools.Log;
using WinFwk.UIMessages;
using MemoScope.Core.Cache;
using MemoScope.Core.Data;
using MemoScope.Core.Bookmark;
using System.Threading;

namespace MemoScope.Core
{
    public class ClrDump
    {
        private static int n = 0;
        public int Id { get; }
        public ClrRuntime Runtime { get; set; }
        public DataTarget Target { get; }
        public string DumpPath { get; }
        public ClrHeap Heap => Runtime.GetHeap();
        public MessageBus MessageBus { get; }
        public BookmarkMgr BookmarkMgr { get; }

        public IList<ClrSegment> Segments => Runtime.GetHeap().Segments;
        public List<ClrMemoryRegion> Regions => Runtime.EnumerateMemoryRegions().ToList();
        public IList<ClrModule> Modules => Runtime.Modules;

        public List<ClrHandle> Handles => Runtime.EnumerateHandles().ToList();
        public List<ulong> FinalizerQueueObjectAddresses => Runtime.EnumerateFinalizerQueueObjectAddresses().ToList();
        public IEnumerable<IGrouping<ClrType, ulong>> FinalizerQueueObjectAddressesByType => Runtime.EnumerateFinalizerQueueObjectAddresses().GroupBy( address => GetObjectType(address));
        public IEnumerable<ClrRoot> ClrRoots => Runtime.GetHeap().EnumerateRoots();
        public List<BlockingObject> BlockingObjects => Runtime.GetHeap().EnumerateBlockingObjects().ToList();
        public IList<ClrThread> Threads => Runtime.Threads;
        public ClrThreadPool ThreadPool => Runtime.GetThreadPool();
        public List<ClrType> AllTypes => Heap.EnumerateTypes().ToList();

        public Dictionary<int, ThreadProperty> ThreadProperties
        {
            get
            {
                if (threadProperties == null)
                {
                    InitThreadProperties();
                }
                return threadProperties;
            }
        }


        Dictionary<int, ThreadProperty> threadProperties;
        private readonly SingleThreadWorker worker;
        private ClrDumpCache cache;
        
        public ClrDump(DataTarget target, string dumpPath, MessageBus msgBus)
        {
            Id = n++;
            Target = target;
            DumpPath = dumpPath;
            MessageBus = msgBus;
            worker = new SingleThreadWorker(dumpPath);
            worker.Run(InitRuntime);

            BookmarkMgr = new BookmarkMgr(dumpPath);
        }

        public void InitCache(CancellationToken token)
        {
            cache = new ClrDumpCache(this);
            cache.Init(token);
        }

        private void InitRuntime()
        {
            MessageBus.Log(this, "InitRuntime: " + DumpPath);
            using (var locator = DacFinderFactory.CreateDactFinder("DacSymbols"))
            {
                var clrVersion = Target.ClrVersions[0];
                var dacFile = locator.FindDac(clrVersion);
                Runtime = clrVersion.CreateRuntime(dacFile);
            }
        }

        public List<ClrType> GetTypes()
        {
            List<ClrType> t = worker.Eval( () => t = AllTypes);
            return t;
        }

        internal void Destroy()
        {
            Dispose();
            cache.Destroy();
        }

        internal void Dispose()
        {
            cache.Dispose();
            Runtime.DataTarget.Dispose();
        }

        public ClrType GetClrType(string typeName)
        {
            ClrType t = worker.Eval(() => t = Heap.EnumerateTypes().FirstOrDefault(clrType => clrType.Name == typeName));
            return t;
        }

        public List<ClrTypeStats> GetTypeStats()
        {
            var stats = cache.LoadTypeStat();
            return stats;
        }

        public List<ulong> GetInstances(ClrType type)
        {
            int typeId = cache.GetTypeId(type.Name);
            return GetInstances(typeId);
        }

        public IEnumerable<ulong> EnumerateInstances(ClrType type)
        {
            int typeId = cache.GetTypeId(type.Name);
            return cache.EnumerateInstances(typeId);
        }

        public List<ulong> GetInstances(int typeId)
        {
            var instances = cache.LoadInstances(typeId);
            return instances;
        }

        public ClrType GetType(ulong methodTable)
        {
            var type = Eval(() => Heap.GetTypeByMethodTable(methodTable));
            return type;
        }

        public bool IsString(ClrType type)
        {
            var res = Eval(() => type.IsString);
            return res;
        }
        public bool IsPrimitive(ClrType type)
        {
            var res = Eval(() => type.IsPrimitive);
            return res;
        }

        public T Eval<T>(Func<T> func)
        {
            return worker.Eval(func);
        }
        public void Run(Action action)
        {
            worker.Run(action);
        }
        public object GetSimpleValue(ulong address, ClrType type)
        {
            var obj = Eval(() => GetSimpleValueImpl(address, type));
            return obj;
        }

        private object GetSimpleValueImpl(ulong address, ClrType type)
        {
            if (SimpleValueHelper.IsSimpleValue(type))
            {
                var value = SimpleValueHelper.GetSimpleValue(address, type, false);
                return value;
            }
            
            return address;
        }

        public object GetFieldValue(ulong address, ClrType type, List<ClrInstanceField> fields)
        {
            var obj = Eval(() => GetFieldValueImpl(address, type, fields));
            return obj;
        }
        public object GetFieldValue(ulong address, ClrType type, ClrInstanceField field)
        {
            var obj = Eval(() => GetFieldValueImpl(address, type, field));
            return obj;
        }

        public object GetFieldValueImpl(ulong address, ClrType type, List<ClrInstanceField> fields)
        {
            ClrObject obj = new ClrObject(address, type);

            for (int i = 0; i < fields.Count; i++)
            {
                var field = fields[i];
                obj = obj[field];
                if( obj.IsNull )
                {
                    return null;
                }
            }

            return obj.HasSimpleValue ? obj.SimpleValue : obj.Address;
        }

        public object GetFieldValueImpl(ulong address, ClrType type, ClrInstanceField field)
        {
            ClrObject obj = new ClrObject(address, type);
            var fieldValue = obj[field];
            if (fieldValue.IsNull)
            {
                return null;
            }

            return fieldValue.HasSimpleValue ? fieldValue.SimpleValue : fieldValue.Address;
        }

        public List<ulong> GetReferences(ulong address)
        {
            var references = cache.LoadReferences(address);
            return references;
        }

        public bool HasReferences(ulong address)
        {
            var hasReferences = cache.HasReferences(address);
            return hasReferences;
        }

        public string GetObjectTypeName(ulong address)
        {
            var name = worker.Eval(() =>
            {
                var clrType = Heap.GetObjectType(address);
                return clrType?.Name;
            });
            return name;
        }

        public ClrType GetObjectType(ulong address)
        {
            var clrType = worker.Eval(() => Heap.GetObjectType(address));
            return clrType;
        }

        private void InitThreadProperties()
        {
            ClrType threadType = GetClrType(typeof(Thread).FullName);
            var threadsInstances = GetInstances(threadType);
            var nameField = threadType.GetFieldByName("m_Name");
            var priorityField = threadType.GetFieldByName("m_Priority");
            var idField = threadType.GetFieldByName("m_ManagedThreadId");

            threadProperties = new Dictionary<int, ThreadProperty>();
            foreach (ulong threadAddress in threadsInstances)
            {
                string name = (string)GetFieldValue(threadAddress, threadType, nameField);
                int priority = (int)GetFieldValue(threadAddress, threadType, priorityField);
                int id = (int)GetFieldValue(threadAddress, threadType, idField);
                threadProperties[id] = new ThreadProperty
                {
                    Address = threadAddress,
                    ManagedId = id,
                    Priority = priority,
                    Name = name
                };
            }
        }
    }

    public class ThreadProperty
    {
        public ulong Address { get; set; }
        public string Name { get; set; }
        public int Priority { get; set; }
        public int ManagedId { get; set; }
    }
}