using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace Samples.DONSSystem
{
    [UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
    [UpdateAfter(typeof(ReceiveSystem))]
    public partial class SPFSystem : SystemBase
    {
        private EndFixedStepSimulationEntityCommandBufferSystem ecbSystem;
        private int frameCount = -1;
        private int epoach = 500000;
        protected override void OnCreate()
        {
            ecbSystem = World.GetExistingSystem<EndFixedStepSimulationEntityCommandBufferSystem>();
            this.Enabled = false;
        }
        protected override void OnUpdate()
        {
            frameCount++;
            frameCount %= epoach;
            if (frameCount % 1000 == 0)
            {
                Debug.Log(String.Format("spf frame {0:d}", frameCount));
            }
            if (frameCount == 10000)
            {
                Entities.ForEach((Entity switchEntity, int entityInQueryIndex, ref SwitchData switchData, ref DynamicBuffer<RoutingEntry> routingEntries, ref DynamicBuffer<TopoEntry> topoEntries) =>
                {
                    //SPF
                    routingEntries.Clear();
                    int n = switchData.switch_node + switchData.host_node;
                    int fatTreeK = switchData.fattree_K;
                    int source = switchData.switch_id;
                    NativeArray<int> adjcencies = new(n * fatTreeK, Allocator.TempJob);
                    NativeArray<int> adjcencyCount = new(n, Allocator.TempJob);
                    NativeArray<int> distance = new(n, Allocator.TempJob);
                    NativeArray<int> nextHops = new(n * fatTreeK, Allocator.TempJob);
                    NativeArray<int> nextHopCount = new(n, Allocator.TempJob);
                    NativeArray<bool> included = new(n, Allocator.TempJob);
                    for (int i = 0; i < n; i++)
                    {
                        adjcencyCount[i] = 0;
                        distance[i] = int.MaxValue;
                        nextHopCount[i] = 0;
                        included[i] = false;
                    }
                    var topos = topoEntries.AsNativeArray();
                    for (int i =0; i < topos.Length; i++)
                    {
                        var topo = topos[i];
                        if (!topo.isValid) continue;
                        var v1 = topo.vertexl_id;
                        var v2 = topo.vertexg_id;
                        adjcencies[v1 * fatTreeK + adjcencyCount[v1]] = v2;
                        adjcencyCount[v1]++;
                        adjcencies[v2 * fatTreeK + adjcencyCount[v2]] = v1;
                        adjcencyCount[v2]++;
                    }
                    //Debug.Log(String.Format("switch {0:d} topo info", source));
                    for (int i = 0; i < n; i++)
                    {
                        Debug.Log(String.Format("In the topo of switch {1:d} node {0:d} is connected with {2:d} node", i, source, adjcencyCount[i]));
                        /*for (int j = 0; j < adjcencyCount[i]; j++)
                        {
                            Debug.Log(String.Format("({0:d},{1:d}) ", i, adjcencies[i * fatTreeK + j]));
                        }*/
                    }
                    distance[source] = 0;
                    included[source] = true;
                    for (int i = 0; i < adjcencyCount[source]; i++)
                    {
                        var adjcency = adjcencies[source * fatTreeK + i];
                        distance[adjcency] = 1;
                        nextHops[adjcency * fatTreeK + nextHopCount[adjcency]] = adjcency;
                        nextHopCount[adjcency]++;
                    }
                    for (int i = 0; i < n - 1; i++)
                    {
                        int minDis = int.MaxValue;
                        int next = -1;
                        //寻找下一个加入节点
                        for (int j = 0; j < n; j++)
                        {
                            if (included[j]) continue;
                            
                            if (minDis > distance[j])
                            {
                                minDis = distance[j];
                                next = j;
                            }
                        }
                        //更新下一个加入节点的邻居的距离
                        included[next] = true;
                        //Debug.Log(String.Format("next id :{0:d}, distance {1:d} ", next, distance[next]));
                        for (int j = 0; j < adjcencyCount[next]; j++)
                        {
                            var adjcency = adjcencies[next * fatTreeK + j];
                            //发现新的最小路径
                            if (distance[adjcency] > minDis + 1)
                            {
                                distance[adjcency] = minDis + 1;
                                nextHopCount[adjcency] = nextHopCount[next];
                                for (int k = 0; k < nextHopCount[next]; k++)
                                {
                                    nextHops[adjcency * fatTreeK + k] = nextHops[next * fatTreeK + k];
                                }
                            }
                            //等价路由
                            else if (distance[adjcency] == minDis + 1)
                            {
                                NativeArray<bool> isDuplicateNextHop = new(nextHopCount[next], Allocator.TempJob);
                                //甄别重复的next_hop
                                for (int k = 0; k < nextHopCount[next]; k++)
                                {
                                    isDuplicateNextHop[k] = false;
                                    for (int l = 0; l < nextHopCount[adjcency]; l++)
                                    {
                                        if (nextHops[adjcency * fatTreeK + l] == nextHops[next * fatTreeK + l])
                                        {
                                            isDuplicateNextHop[k] = true;
                                            break;
                                        }
                                    }
                                }
                                //将不重复的next_hop加入adjcency的next_hop数组中
                                for (int k = 0; k < nextHopCount[next]; k++)
                                {
                                    if (!isDuplicateNextHop[k])
                                    {
                                        nextHops[adjcency * fatTreeK + nextHopCount[adjcency]] = nextHops[next * fatTreeK + k];
                                        nextHopCount[adjcency]++;
                                    }
                                }
                                isDuplicateNextHop.Dispose();
                            }
                        }
                    }
                    for (int i = 0; i < n; i++)
                    {
                        if (i == source) continue;
                        for (int j = 0; j < nextHopCount[i]; j++)
                        {
                            routingEntries.Add(new RoutingEntry
                            {
                                dest_id = i,
                                next_hop = nextHops[i * fatTreeK + j],
                                distance = distance[i]
                            }) ;
                        }
                    }
                    //LOG
                    var routings = routingEntries.AsNativeArray();
                    Debug.Log("-----------------------");
                    Debug.Log(String.Format("Routing Table of switch {0:d}", source));
                    for (int i = 0; i < routings.Length; i++)
                    {
                        var routingEntry = routings[i];
                        Debug.Log(String.Format(" routingTableEntry of switch {3:d} dest_id: {0:d}, next_hop: {1:d}, distance: {2:d} ", routingEntry.dest_id, routingEntry.next_hop, routingEntry.distance, source));
                    }
                    Debug.Log("-----------------------");
                    adjcencies.Dispose();
                    adjcencyCount.Dispose();
                    distance.Dispose();
                    included.Dispose();
                    nextHopCount.Dispose();
                    nextHops.Dispose();
                }).ScheduleParallel();
                ecbSystem.AddJobHandleForProducer(Dependency);
            }
        }

    }
}