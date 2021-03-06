﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using AntDiary.Scripts.Roads;
using UniRx;
using UniRx.Triggers;
using UnityEngine;
using Random = UnityEngine.Random;

namespace AntDiary
{
    /// <summary>
    /// 巣統合システム。
    /// シングルトンで動くので、NestSystem.Instanceでシングルトンインスタンスにアクセス可能。
    /// </summary>
    public class NestSystem : MonoBehaviour, IDebugMenu
    {
        public NestData Data => GameContext.Current?.s_NestData;

        #region Singleton Implementation

        public static NestSystem Instance { get; private set; }

        /// <summary>
        /// 自身をSingletonのインスタンスとして登録。既に別のインスタンスが存在する場合はfalseを返す。
        /// </summary>
        /// <returns></returns>
        private bool RegisterSingletonInstance()
        {
            if (!Instance)
            {
                Instance = this;
                return true;
            }
            else
            {
                return false;
            }
        }

        #endregion

        private void Awake()
        {
            if (!RegisterSingletonInstance())
            {
                Destroy(gameObject);
                return;
            }
        }

        [SerializeField] private AntFactory[] antFactories = default;
        [SerializeField] private NestElementFactory[] nestElementFactories = default;

        public BuildingSystem BuildingSystem { get; set; }

        private readonly List<Ant> spawnedAnts = new List<Ant>();

        /// <summary>
        /// 巣に存在するすべてのアリを取得する。
        /// </summary>
        public IReadOnlyList<Ant> SpawnedAnt => spawnedAnts;

        private readonly List<NestElement> nestElements = new List<NestElement>();

        /// <summary>
        /// 巣に存在するすべてのNestElementを取得する。
        /// </summary>
        public IReadOnlyList<NestElement> NestElements => nestElements;

        private readonly List<NestPathElementEdge> elementEdges = new List<NestPathElementEdge>();

        /// <summary>
        /// NestElement間の接続をすべて取得する。
        /// </summary>
        public IReadOnlyList<NestPathElementEdge> ElementEdges => elementEdges;

        /// <summary>
        /// 建築中のNestElementをすべて取得する。
        /// </summary>
        public IEnumerable<NestBuildableElement> BuildingElements =>
            nestElements.OfType<NestBuildableElement>().Where(d => d.IsUnderConstruction);

        /// <summary>
        /// 巣に存在するすべてのIPathNodeを取得する。
        /// </summary>
        public IEnumerable<NestPathNode> NestPathNodes => NestElements.SelectMany(e => e.GetNodes());

        private void Start()
        {
            if (SaveUnit.Current != null)
            {
                //すでにロード済み
                LoadData();
            }

            //次にセーブデータが変更（ロード）されたときに、巣を更新する
            SaveUnit.OnCurrentSaveUnitChanged.Subscribe(su => LoadData());

            BuildingSystem = new BuildingSystem(this);
        }

        /// <summary>
        /// NestDataをもとに巣を再構築する。
        /// </summary>
        private void LoadData()
        {
            //生成済みのアリを破棄
            foreach (var ant in spawnedAnts)
            {
                Destroy(ant.gameObject);
            }

            spawnedAnts.Clear();

            //生成済みの部屋や道を破棄
            foreach (var element in nestElements)
            {
                Destroy(element.gameObject);
            }

            nestElements.Clear();

            elementEdges.Clear();


            //セーブデータから巣情報をロード

            //道、部屋を生成
            foreach (var elementData in Data.Structure.NestElements)
            {
                var element = InstantiateNestElement(elementData, false);
            }
            
            //Element間の接続をロード
            foreach (var edgeData in Data.Structure.ElementEdges)
            {
                var edge = new NestPathElementEdge(nestElements, edgeData);
                elementEdges.Add(edge);
            }
            
            //アリを生成
            foreach (var antData in Data.Ants)
            {
                var ant = InstantiateAnt(antData, false);
            }

        }


        /// <summary>
        /// AntDataをもとに、対応するAntFactoryを用いてAntのインスタンスを生成する。
        /// </summary>
        /// <param name="antData">生成に使用するAntData。</param>
        /// <param name="registerToGameContext">新たにGameContextに登録するかどうか。セーブデータからの生成などの際に限りfalseを指定する。</param>
        /// <returns>生成されたGameObjectのもつAntコンポーネント。</returns>
        public Ant InstantiateAnt(AntData antData, bool registerToGameContext = true)
        {
            AntFactory matchedFactory = null;
            foreach (var f in antFactories)
            {
                if (f.DataType == antData.GetType())
                {
                    matchedFactory = f;
                    break;
                }
                else if (matchedFactory == null && antData.GetType().IsSubclassOf(f.DataType))
                {
                    matchedFactory = f;
                }
            }

            if (!matchedFactory) Debug.LogWarning($"NestSystem: {antData.GetType()} 用のAntFactoryは登録されていません。");

            var ant = matchedFactory?.InstantiateAnt(antData);

            if (ant != null)
            {
                if (registerToGameContext)
                {
                    Data.Ants.Add(antData);
                }

                spawnedAnts.Add(ant);
            }

            return ant;
        }

        /// <summary>
        /// InstantiateAntで生成したアリを破棄します。
        /// セーブデータから該当のアリを削除します。
        /// GameObjectの破棄までは担当しないので、呼び出し側でDestroyしてください。
        /// </summary>
        /// <param name="ant"></param>
        public void RemoveAnt(Ant ant)
        {
            if (Data.Ants.Contains(ant.Data))
            {
                Data.Ants.Remove(ant.Data);
            }
        }


        /// <summary>
        /// NestElementDataをもとに、対応するNestElementFactoryを用いてNestElementのインスタンスを生成する。
        /// </summary>
        /// <param name="elementData">生成に使用するNestElementData。</param>
        /// <param name="registerToGameContext">新たにGameContextに登録するかどうか。セーブデータからの生成などの際に限りfalseを指定する。</param>
        /// <returns>生成されたGameObjectのもつNestElementコンポーネント。</returns>
        public NestElement InstantiateNestElement(NestElementData elementData, bool registerToGameContext = true,
            bool registerInstanceToNestSystem = true)
        {
            NestElementFactory matchedFactory = null;
            foreach (var f in nestElementFactories)
            {
                if (f.DataType == elementData.GetType())
                {
                    matchedFactory = f;
                    break;
                }
                else if (matchedFactory == null && elementData.GetType().IsSubclassOf(f.DataType))
                {
                    matchedFactory = f;
                }
            }

            if (!matchedFactory)
                Debug.LogWarning($"NestSystem: {elementData.GetType()} 用のNestElementFactoryは登録されていません。");

            var nestElement = matchedFactory?.InstantiateNestElement(elementData);

            if (nestElement != null)
            {
                if (registerToGameContext)
                {
                    Data.Structure.NestElements.Add(elementData);
                }

                if (registerInstanceToNestSystem)
                {
                    nestElements.Add(nestElement);
                }
            }

            return nestElement;
        }

        /// <summary>
        /// InstantiateNestElement()で追加したNestElementを削除します。
        /// セーブデータからNestElementを削除し、関連するElement間接続を破棄します。
        /// GameObjectの破棄までは担当しないので、呼び出し側でDestroyしてください
        /// </summary>
        /// <param name="element"></param>
        public void RemoveNestElement(NestElement element)
        {
            if (Data.Structure.NestElements.Contains(element.Data))
            {
                Data.Structure.NestElements.Remove(element.Data);
                nestElements.Remove(element);
                //Element間接続がある場合はその接続も破棄
                var l = elementEdges.Where(e => e.A.Host == element || e.B.Host == element).ToList();
                foreach (var e in l)
                {
                    if (Data.Structure.ElementEdges.Contains(e.Data))
                    {
                        Data.Structure.ElementEdges.Remove(e.Data);
                    }

                    e.Clear();
                    elementEdges.Remove(e);
                }
            }
        }

        /// <summary>
        /// 外部で生成したNestElementをNestSystemに登録する。
        /// あらかじめNestElement.Initializeで適切なNestElementDataが注入されている必要がある。
        /// </summary>
        /// <param name="element"></param>
        public void RegisterNestElementToGameContext(NestElement element)
        {
            if (element.Data == null)
                throw new ArgumentException("NestElementDataが設定されていません。あらかじめ適切なNestElementDataを注入してください。");
            if (Data.Structure.NestElements.Contains(element.Data))
                throw new ArgumentException("指定されたNestElementのDataはすでに登録されています。");

            if (!nestElements.Contains(element)) nestElements.Add(element);

            Data.Structure.NestElements.Add(element.Data);
        }

        public NestPathElementEdge ConnectElements(NestPathNode a, NestPathNode b)
        {
            var edge = new NestPathElementEdge(a, b);

            elementEdges.Add(edge);
            Data.Structure.ElementEdges.Add(edge.Data);
            return edge;
        }

        public IEnumerable<T> GetNestElements<T>() where T : NestElement
        {
            return nestElements.OfType<T>();
        }
        
        public IEnumerable<T> GetAnts<T>() where T : Ant
        {
            return spawnedAnts.OfType<T>();
        }

        public T GetNestElement<T>() where T : NestElement
        {
            return GetNestElements<T>().FirstOrDefault();
        }
        
        public T GetAnt<T>() where T : Ant
        {
            return GetAnts<T>().FirstOrDefault();
        }

        /// <summary>
        /// [GUID]/[Name of node]で構成されたパスからノードを取得する。
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
        public NestPathNode GetNodeFromPath(string path)
        {
            ParseNodePath(path, out string guid, out string name);
            return nestElements.FirstOrDefault(e => e.Data.Guid == guid)?.GetNodes().FirstOrDefault(n => n.Name == name);
        }

        public static void ParseNodePath(string path, out string guid, out string name)
        {
            var splitted = path.Split('/');
            if(splitted.Length != 2) throw new ArgumentException();

            guid = splitted[0];
            name = splitted[1];
        }

        public IEnumerable<IPathNode> FindRoute(NestPathNode from, NestPathNode to)
        {
            AStarSearcher searcher = new AStarSearcher(null);
            searcher.SearchRoute(from, to);
            return searcher.Route;
        }


        #region Debug

        public string pageTitle { get; } = "巣統合システム";

        // private bool showGraph = true;
        private IEnumerable<IPathNode> latestRoute;
        private List<TypeInfo> antDataTypes;
        private List<TypeInfo> nestElementDataTypes;
        private bool showNestElementData = false;
        private bool showCommonData = false;

        public void OnGUIPage()
        {
            GUILayout.Label($"データのロード: {(Data != null ? "済" : "未")}");
            if (Data != null)
            {
                GUILayout.Label($"SpawnedAnts: {spawnedAnts.Count}");
                
                GUILayout.BeginHorizontal();
                GUILayout.Label($"NestElements: {nestElements.Count}");
                if (showNestElementData && GUILayout.Button("隠す"))
                {
                    showNestElementData = false;
                }
                else if (!showNestElementData && GUILayout.Button("表示"))
                {
                    showNestElementData = true;
                }

                GUILayout.EndHorizontal();
                if (showNestElementData)
                {
                    StringBuilder sb = new StringBuilder();
                    foreach (var d in Data.Structure.NestElements)
                    {
                        if (d == null)
                        {
                            var c = GUI.contentColor;
                            GUI.contentColor = Color.red;
                            GUILayout.Label("(null)");
                            GUI.contentColor = c;
                            continue;
                        }
                        
                        var t = d.GetType();
                        var props = t.GetProperties().Where(p => p.CanRead);

                        foreach (var p in props)
                        {
                            sb.Append($"{p.Name} = {p.GetValue(d)}, ");
                        }
                        
                        GUILayout.Label($"{t.Name} ({sb})");
                        
                        sb.Clear();
                    }
                }
                

                GUILayout.BeginHorizontal();
                GUILayout.Label($"AntStatusRegistry");
                if (showCommonData && GUILayout.Button("隠す"))
                {
                    showCommonData = false;
                }
                else if (!showCommonData && GUILayout.Button("表示"))
                {
                    showCommonData = true;
                }

                GUILayout.EndHorizontal();

                if (showCommonData)
                {
                    GUILayout.BeginHorizontal();
                    int i = 0;
                    foreach (var status in Data.CommonDataRegistry.CommonData)
                    {
                        if (i % 2 == 0)
                        {
                            GUILayout.EndHorizontal();
                            GUILayout.BeginHorizontal();
                        }

                        i++;

                        GUILayout.BeginVertical();

                        var to = status.GetType();
                        var t = to;
                        while (t.IsSubclassOf(typeof(AntCommonDataBase)))
                        {
                            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(AntCommonData<>))
                            {
                                GUILayout.Label($"  {to.Name} (for {t.GenericTypeArguments[0]})");
                                var props = t.GetProperties().Where(p => p.CanRead);
                                foreach (var p in props)
                                {
                                    GUILayout.Label($"   - {p.Name}: {p.GetValue(status)}");
                                }
                            }

                            t = t.BaseType;
                        }

                        GUILayout.EndVertical();
                    }

                    GUILayout.EndHorizontal();
                }

                GUILayout.Label("アリのスポーンテスト");
                if (antDataTypes == null)
                    antDataTypes = antFactories.Select(f => f.GetType().BaseType.GenericTypeArguments[0].GetTypeInfo()).ToList();
                {
                    int i = 0;
                    GUILayout.BeginHorizontal();
                    foreach (var antDataType in antDataTypes)
                    {
                        if (i % 4 == 0)
                        {
                            GUILayout.EndHorizontal();
                            GUILayout.BeginHorizontal();
                        }

                        i++;

                        if (GUILayout.Button(antDataType.Name))
                        {
                            var constructor = antDataType.GetConstructor(new Type[] { });
                            var data = (AntData) constructor?.Invoke(new object[] { });
                            if (data != null)
                            {
                                InstantiateAnt(data);
                            }
                        }
                    }

                    GUILayout.EndHorizontal();
                }
                GUILayout.Label("NEstElementのスポーンテスト");
                if (nestElementDataTypes == null)
                    nestElementDataTypes = nestElementFactories.Select(f => f.GetType().BaseType.GenericTypeArguments[0].GetTypeInfo()).ToList();
                {
                    int i = 0;
                    GUILayout.BeginHorizontal();
                    foreach (var nestElementDataType in nestElementDataTypes)
                    {
                        if (i % 4 == 0)
                        {
                            GUILayout.EndHorizontal();
                            GUILayout.BeginHorizontal();
                        }

                        i++;

                        if (GUILayout.Button(nestElementDataType.Name))
                        {
                            var constructor = nestElementDataType.GetConstructor(new Type[] { });
                            var data = (NestElementData) constructor?.Invoke(new object[] { });
                            if (data != null)
                            {
                                InstantiateNestElement(data);
                            }
                        }
                    }

                    GUILayout.EndHorizontal();
                }


                if (GUILayout.Button("デバッグアリのスポーン"))
                {
                    InstantiateAnt(new DebugAntData());
                }

                if (GUILayout.Button("デバッグ巣の生成"))
                {
                    //生成済みの巣のクリア
                    var ne = nestElements.ToList();
                    foreach (var e in ne)
                    {
                        RemoveNestElement(e);
                    }

                    for (int y = 0; y < 3; y++)
                    for (int x = 0; x < 4; x++)
                    {
                        float posx = x * 4f - 1.5f * 4f;
                        float posy = y * 3f - 1f * 3f;
                        InstantiateNestElement(new DebugRoomData()
                            {Position = new Vector2(posx, posy), IsUnderConstruction = false});
                    }

                    for (int y = 0; y < 3; y++)
                    for (int x = 0; x < 3; x++)
                    {
                        int i = y * 4 + x;
                        var n1 = NestElements[i].GetNodes().First(n => n.Name == "right");
                        var n2 = NestElements[i + 1].GetNodes().First(n => n.Name == "left");

                        var road = InstantiateNestElement(new DebugRoadData()
                            {From = n1.WorldPosition, To = n2.WorldPosition, IsUnderConstruction = false});
                        var roadNodes = road.GetNodes();
                        var r1 = roadNodes.ElementAt(0);
                        var r2 = roadNodes.ElementAt(1);

                        ConnectElements(n1, r1);
                        ConnectElements(r2, n2);
                    }


                    for (int y = 0; y < 2; y++)
                    for (int x = 0; x < 4; x++)
                    {
                        int i = y * 4 + x;
                        var n1 = NestElements[i].GetNodes().First(n => n.Name == "top");
                        var n2 = NestElements[i + 4].GetNodes().First(n => n.Name == "bottom");

                        var road = InstantiateNestElement(new DebugRoadData()
                            {From = n1.WorldPosition, To = n2.WorldPosition, IsUnderConstruction = false});
                        var roadNodes = road.GetNodes();
                        var r1 = roadNodes.ElementAt(0);
                        var r2 = roadNodes.ElementAt(1);

                        ConnectElements(n1, r1);
                        ConnectElements(r2, n2);
                    }
                }

                if (GUILayout.Button("ランダムな経路探索のテスト"))
                {
                    var ne = NestElements[Random.Range(0, NestElements.Count)];
                    var node1 = ne.GetNodes().ElementAt(Random.Range(0, ne.GetNodes().Count()));
                    ne = NestElements[Random.Range(0, NestElements.Count)];
                    var node2 = ne.GetNodes().ElementAt(Random.Range(0, ne.GetNodes().Count()));

                    latestRoute = FindRoute(node1, node2);
                }

                RoadGizmosUtil.ShowNode = GUILayout.Toggle(RoadGizmosUtil.ShowNode, "Nodeを表示");
                RoadGizmosUtil.ShowEdge = GUILayout.Toggle(RoadGizmosUtil.ShowEdge, "Edgeを表示");
                RoadGizmosUtil.ShowElement = GUILayout.Toggle(RoadGizmosUtil.ShowElement, "Elementのコライダを表示");
            }
        }

        private void OnDrawGizmos()
        {
            // if (!showGraph) return;

            foreach (var elem in nestElements)
            {
                if (elem is NestBuildableElement buildableElement)
                {
                    RoadGizmosUtil.DrawBuildableElement(buildableElement);
                }

                RoadGizmosUtil.DrawEdge(elem.GetLocalEdges());
            }

            RoadGizmosUtil.DrawNode(NestPathNodes);
            RoadGizmosUtil.DrawEdge(ElementEdges);

            if (latestRoute != null)
            {
                Gizmos.color = Color.yellow;
                for (int i = 0; i < latestRoute.Count() - 1; i++)
                {
                    var a = latestRoute.ElementAt(i);
                    var b = latestRoute.ElementAt(i + 1);
                    Gizmos.DrawLine(a.WorldPosition, b.WorldPosition);
                }
            }
        }

        #endregion
    }
}