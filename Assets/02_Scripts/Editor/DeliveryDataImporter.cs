using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 배달 데이터 엑셀(DeliveryData.xlsx) → ScriptableObject / 씬 DeliveryPoint 반영
/// Tools → NeonRider → Delivery Data
///  - Import Excel : 엑셀을 읽어서
///      Stores        → StoreData 에셋 생성/갱신 (id로 매칭, 처음엔 이름으로 매칭해서 id 채움)
///      Foods         → FoodData 에셋 생성/갱신, store_id 기준으로 가게 메뉴 연결
///      Destinations  → 열린 씬의 DeliveryPoint 이름/주소/분류 갱신 (dp_id로 매칭)
///      그리고 탭마다 CSV(UTF-8)로 저장 (git 비교, AI 확인용)
///  - Open Excel : 엑셀 파일 열기
/// 에셋은 지우지 않고 값만 덮어씀 → 씬에서 연결해 둔 참조 유지
/// 엑셀을 열어둔 채로도 읽기는 됨 (저장한 내용만 반영되니 엑셀에서 저장 먼저)
/// </summary>
public static class DeliveryDataImporter
{
    const string Root = "Assets/10_Datas/DeliveryData";
    const string ExcelPath = Root + "/DeliveryData.xlsx";
    const string CsvDir = Root + "/CSV";
    const string StoreDir = "Assets/10_Datas/Stores";
    const string FoodDir = "Assets/10_Datas/Foods";

    [MenuItem("Tools/NeonRider/Delivery Data/Import Excel", priority = 0)]
    static void ImportMenu() => Import();

    [MenuItem("Tools/NeonRider/Delivery Data/Open Excel", priority = 1)]
    static void OpenExcel()
    {
        if (!File.Exists(ExcelPath)) { Debug.LogError($"[DeliveryData] 엑셀 없음: {ExcelPath}"); return; }
        EditorUtility.OpenWithDefaultApp(System.IO.Path.GetFullPath(ExcelPath));
    }

    public static void Import()
    {
        if (!File.Exists(ExcelPath)) { Debug.LogError($"[DeliveryData] 엑셀 없음: {ExcelPath}"); return; }

        Dictionary<string, List<string[]>> book;
        try { book = XlsxReader.Read(ExcelPath); }
        catch (Exception e) { Debug.LogError($"[DeliveryData] 엑셀 읽기 실패: {e.Message}"); return; }

        var log = new StringBuilder();
        var warn = new StringBuilder();

        var stores = Table(book, "Stores", warn);
        var foods = Table(book, "Foods", warn);
        var dests = Table(book, "Destinations", warn);
        if (stores == null || foods == null) { Debug.LogError("[DeliveryData] Stores / Foods 탭이 필요합니다\n" + warn); return; }

        EnsureFolder(StoreDir);
        EnsureFolder(FoodDir);

        // ---------- Stores ----------
        var allStores = FindAssets<StoreData>();
        var storeById = new Dictionary<string, StoreData>();
        foreach (var row in stores)
        {
            string id = row.Get("id"), name = row.Get("name");
            if (storeById.ContainsKey(id)) { warn.AppendLine($"Stores: id 중복 '{id}' (행 {row.Line}) → 건너뜀"); continue; }

            var s = allStores.FirstOrDefault(x => x.id == id)
                 ?? allStores.FirstOrDefault(x => string.IsNullOrEmpty(x.id) && x.storeName == name);
            bool created = false;
            if (s == null)
            {
                s = ScriptableObject.CreateInstance<StoreData>();
                s.id = id;
                AssetDatabase.CreateAsset(s, AssetDatabase.GenerateUniqueAssetPath($"{StoreDir}/Store_{Safe(name, id)}.asset"));
                allStores.Add(s);
                created = true;
            }
            Undo.RecordObject(s, "Import Delivery Data");
            string before = Snapshot(s);
            s.id = id;
            s.storeName = string.IsNullOrEmpty(name) ? id : name;
            s.available = row.Bool("available", true);
            storeById[id] = s;
            Note(log, created, before, Snapshot(s), "Store", id, s);
        }

        // ---------- Foods ----------
        var allFoods = FindAssets<FoodData>();
        var foodById = new Dictionary<string, FoodData>();
        var menus = storeById.Keys.ToDictionary(k => k, k => new List<FoodData>());
        foreach (var row in foods)
        {
            string id = row.Get("id"), name = row.Get("name"), storeId = row.Get("store_id");
            if (foodById.ContainsKey(id)) { warn.AppendLine($"Foods: id 중복 '{id}' (행 {row.Line}) → 건너뜀"); continue; }

            var f = allFoods.FirstOrDefault(x => x.id == id)
                 ?? allFoods.FirstOrDefault(x => string.IsNullOrEmpty(x.id) && x.displayName == name);
            bool created = false;
            if (f == null)
            {
                f = ScriptableObject.CreateInstance<FoodData>();
                f.id = id;
                AssetDatabase.CreateAsset(f, AssetDatabase.GenerateUniqueAssetPath($"{FoodDir}/Food_{Safe(name, id)}.asset"));
                allFoods.Add(f);
                created = true;
            }
            Undo.RecordObject(f, "Import Delivery Data");
            string before = Snapshot(f);
            f.id = id;
            f.displayName = string.IsNullOrEmpty(name) ? id : name;
            f.freshness = row.Tier("freshness", f.freshness, warn);
            f.impact = row.Tier("impact", f.impact, warn);
            f.basePay = Mathf.RoundToInt(row.Float("base_pay", f.basePay, warn));
            f.payMultiplier = row.Float("pay_multiplier", f.payMultiplier, warn);
            foodById[id] = f;
            Note(log, created, before, Snapshot(f), "Food", id, f);

            if (string.IsNullOrEmpty(storeId)) warn.AppendLine($"Foods: '{id}' store_id 비어 있음 → 어느 가게 메뉴에도 안 들어감");
            else if (!menus.ContainsKey(storeId)) warn.AppendLine($"Foods: '{id}' store_id '{storeId}'가 Stores 탭에 없음");
            else menus[storeId].Add(f);
        }

        // 메뉴 연결 (엑셀 순서대로)
        foreach (var kv in menus)
        {
            var s = storeById[kv.Key];
            var newMenu = kv.Value.ToArray();
            bool same = s.menu != null && s.menu.SequenceEqual(newMenu);
            if (!same)
            {
                Undo.RecordObject(s, "Import Delivery Data");
                s.menu = newMenu;
                log.AppendLine($"  메뉴 {kv.Key}: {string.Join(", ", newMenu.Select(x => x.id))}");
            }
            EditorUtility.SetDirty(s);
            if (newMenu.Length == 0 && s.available) warn.AppendLine($"Stores: '{kv.Key}' 메뉴가 비어 있음 → 주문이 생기지 않음");
        }
        foreach (var f in foodById.Values) EditorUtility.SetDirty(f);

        // 엑셀에 없는 에셋 (지우지 않고 알려만 줌)
        foreach (var s in allStores.Where(x => !storeById.ContainsValue(x)))
            warn.AppendLine($"엑셀에 없는 StoreData: {AssetDatabase.GetAssetPath(s)} (id '{s.id}')");
        foreach (var f in allFoods.Where(x => !foodById.ContainsValue(x)))
            warn.AppendLine($"엑셀에 없는 FoodData: {AssetDatabase.GetAssetPath(f)} (id '{f.id}')");

        // ---------- Destinations (열린 씬) ----------
        if (dests != null) ApplyDestinations(dests, log, warn);

        AssetDatabase.SaveAssets();

        // ---------- CSV ----------
        ExportCsv(book);
        AssetDatabase.Refresh();

        Debug.Log($"[DeliveryData] 가져오기 완료: 가게 {storeById.Count}, 음식 {foodById.Count}, 배달지 행 {dests?.Count ?? 0}\n{(log.Length > 0 ? log.ToString() : "  바뀐 것 없음\n")}");
        if (warn.Length > 0) Debug.LogWarning("[DeliveryData] 확인 필요\n" + warn);
    }

    // ================= Destinations =================
    static readonly Regex BlockId = new Regex(@"^([A-Z]{2}|BLOCK)\d{3}[A-Z]?-\d+$");

    static void ApplyDestinations(List<Row> dests, StringBuilder log, StringBuilder warn)
    {
        var dps = UnityEngine.Object.FindObjectsByType<DeliveryPoint>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                  .Where(d => d.gameObject.scene.IsValid()).ToList();
        if (dps.Count == 0) { warn.AppendLine("Destinations: 열린 씬에 DeliveryPoint가 없음 (씬을 열고 다시 가져오기)"); return; }

        // dpId 비어 있으면 자동으로 채움: 가게 픽업 = 가게 id, 블록ID-번호(GH014-01) = 그대로, 나머지 = 오브젝트 이름
        var byId = new Dictionary<string, DeliveryPoint>();
        foreach (var dp in dps)
        {
            var so = new SerializedObject(dp);
            var idProp = so.FindProperty("dpId");
            if (string.IsNullOrEmpty(idProp.stringValue))
            {
                var store = so.FindProperty("store").objectReferenceValue as StoreData;
                string disp = so.FindProperty("displayName").stringValue;
                string guess = store != null && !string.IsNullOrEmpty(store.id) ? store.id
                             : BlockId.IsMatch(disp ?? "") ? disp
                             : dp.gameObject.name;
                idProp.stringValue = guess;
                so.ApplyModifiedProperties();
                EditorSceneManager.MarkSceneDirty(dp.gameObject.scene);
                log.AppendLine($"  dpId 자동 지정: {HierPath(dp)} → {guess}");
            }
            if (byId.ContainsKey(idProp.stringValue)) warn.AppendLine($"DeliveryPoint dpId 중복 '{idProp.stringValue}': {HierPath(byId[idProp.stringValue])} / {HierPath(dp)}");
            else byId[idProp.stringValue] = dp;
        }

        var seen = new HashSet<string>();
        foreach (var row in dests)
        {
            string id = row.Get("dp_id");
            seen.Add(id);
            if (!byId.TryGetValue(id, out var dp)) { warn.AppendLine($"Destinations: '{id}'에 해당하는 DeliveryPoint가 열린 씬에 없음"); continue; }

            var so = new SerializedObject(dp);
            bool changed = false;
            changed |= SetIfGiven(so, "displayName", row.Get("display_name"));
            changed |= SetString(so, "address", row.Get("address"));
            changed |= SetString(so, "category", row.Get("category"));
            if (changed)
            {
                so.ApplyModifiedProperties();
                EditorSceneManager.MarkSceneDirty(dp.gameObject.scene);
                log.AppendLine($"  배달지 {id}: {row.Get("display_name")} / {row.Get("address")} / {row.Get("category")}");
            }
        }
        foreach (var kv in byId)
            if (!seen.Contains(kv.Key) && kv.Value.Type == DeliveryPointType.Dropoff)
                warn.AppendLine($"Destinations: 씬의 배달지 '{kv.Key}' ({HierPath(kv.Value)})가 엑셀에 없음");
    }

    // 엑셀 칸이 비어 있으면 기존 값 유지
    static bool SetIfGiven(SerializedObject so, string prop, string value)
    {
        if (string.IsNullOrEmpty(value)) return false;
        return SetString(so, prop, value);
    }

    static bool SetString(SerializedObject so, string prop, string value)
    {
        var p = so.FindProperty(prop);
        if (p == null || p.stringValue == (value ?? "")) return false;
        p.stringValue = value ?? "";
        return true;
    }

    static string HierPath(Component c)
    {
        var t = c.transform; string s = t.name;
        while (t.parent != null) { t = t.parent; s = t.name + "/" + s; }
        return s;
    }

    // ================= CSV =================
    static void ExportCsv(Dictionary<string, List<string[]>> book)
    {
        Directory.CreateDirectory(CsvDir);
        foreach (var kv in book)
        {
            if (kv.Key.StartsWith("_") || kv.Key.Equals("README", StringComparison.OrdinalIgnoreCase)) continue;
            var sb = new StringBuilder();
            foreach (var r in kv.Value)
            {
                if (r.All(string.IsNullOrEmpty)) continue;
                sb.Append(string.Join(",", r.Select(Csv))).Append('\n');
            }
            File.WriteAllText($"{CsvDir}/{kv.Key}.csv", sb.ToString(), new UTF8Encoding(true));
        }
    }

    static string Csv(string v)
    {
        if (v == null) v = "";
        return v.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
    }

    // ================= 표 =================
    class Row
    {
        public int Line;
        public Dictionary<string, string> Cells;
        public string Get(string key) => Cells.TryGetValue(key, out var v) ? v.Trim() : "";
        public bool Bool(string key, bool def)
        {
            string v = Get(key).ToUpperInvariant();
            if (v == "") return def;
            return v == "TRUE" || v == "1" || v == "Y" || v == "YES" || v == "O";
        }
        public FoodSensitivity Tier(string key, FoodSensitivity def, StringBuilder warn)
        {
            string v = Get(key);
            if (v == "") return def;
            if (FoodSensitivityTable.TryParse(v, out var s)) return s;
            warn.AppendLine($"행 {Line}: {key} 값 '{v}'는 낮음/보통/높음/매우높음 중 하나여야 함 → 기존 값 유지");
            return def;
        }

        public float Float(string key, float def, StringBuilder warn)
        {
            string v = Get(key);
            if (v == "") return def;
            if (float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out float f)) return f;
            warn.AppendLine($"행 {Line}: {key} 값 '{v}'가 숫자가 아님 → 기존 값 유지");
            return def;
        }
    }

    // 첫 행 = 헤더 (소문자). id(첫 칸)가 비었거나 #으로 시작하는 행은 건너뜀
    static List<Row> Table(Dictionary<string, List<string[]>> book, string sheet, StringBuilder warn)
    {
        var key = book.Keys.FirstOrDefault(k => k.Equals(sheet, StringComparison.OrdinalIgnoreCase));
        if (key == null) { warn.AppendLine($"'{sheet}' 탭이 없음"); return null; }
        var rows = book[key];
        if (rows.Count == 0) return new List<Row>();
        var header = rows[0].Select(h => (h ?? "").Trim().ToLowerInvariant()).ToArray();
        var list = new List<Row>();
        for (int i = 1; i < rows.Count; i++)
        {
            var r = rows[i];
            string first = r.Length > 0 ? (r[0] ?? "").Trim() : "";
            if (first == "" || first.StartsWith("#")) continue;
            var cells = new Dictionary<string, string>();
            for (int c = 0; c < header.Length; c++)
                if (header[c] != "") cells[header[c]] = c < r.Length ? r[c] ?? "" : "";
            list.Add(new Row { Line = i + 1, Cells = cells });
        }
        return list;
    }

    // ================= 유틸 =================
    static List<T> FindAssets<T>() where T : UnityEngine.Object =>
        AssetDatabase.FindAssets("t:" + typeof(T).Name)
            .Select(g => AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(g)))
            .Where(a => a != null).ToList();

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
    }

    static string Safe(string name, string fallback)
    {
        string s = string.IsNullOrEmpty(name) ? fallback : name;
        foreach (char c in System.IO.Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        return s.Replace(" ", "");
    }

    static string Snapshot(StoreData s) => $"{s.id}|{s.storeName}|{s.available}";
    static string Snapshot(FoodData f) => $"{f.id}|{f.displayName}|{f.freshness}|{f.impact}|{f.basePay}|{f.payMultiplier}";

    static void Note(StringBuilder log, bool created, string before, string after, string kind, string id, UnityEngine.Object asset)
    {
        if (created) log.AppendLine($"  + {kind} {id} 생성: {AssetDatabase.GetAssetPath(asset)}");
        else if (before != after) log.AppendLine($"  ~ {kind} {id} 갱신: {before} → {after}");
    }

    // ================= xlsx 읽기 (외부 DLL 없이) =================
    static class XlsxReader
    {
        static readonly XNamespace Ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        static readonly XNamespace RNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        static readonly XNamespace PNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        /// <summary>탭 이름 → 행 목록 (각 행은 A열부터 문자열 배열)</summary>
        public static Dictionary<string, List<string[]>> Read(string path)
        {
            var result = new Dictionary<string, List<string[]>>();
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Read))
            {

            var shared = new List<string>();
            var sse = zip.GetEntry("xl/sharedStrings.xml");
            if (sse != null)
            {
                using (var st = sse.Open())
                    foreach (var si in XDocument.Load(st).Root.Elements(Ns + "si"))
                        shared.Add(string.Concat(si.Descendants(Ns + "t").Where(t => t.Parent.Name != Ns + "rPh").Select(t => t.Value)));
            }

            var rels = new Dictionary<string, string>();
            using (var st = zip.GetEntry("xl/_rels/workbook.xml.rels").Open())
                foreach (var r in XDocument.Load(st).Root.Elements(PNs + "Relationship"))
                {
                    string target = (string)r.Attribute("Target");
                    target = target.StartsWith("/") ? target.Substring(1) : "xl/" + target;
                    rels[(string)r.Attribute("Id")] = target;
                }

            XElement sheetsEl;
            using (var st = zip.GetEntry("xl/workbook.xml").Open())
                sheetsEl = XDocument.Load(st).Root.Element(Ns + "sheets");

            foreach (var sh in sheetsEl.Elements(Ns + "sheet"))
            {
                string name = (string)sh.Attribute("name");
                string rid = (string)sh.Attribute(RNs + "id");
                if (!rels.TryGetValue(rid, out var entryPath)) continue;
                var entry = zip.GetEntry(entryPath);
                if (entry == null) continue;

                var rows = new SortedDictionary<int, Dictionary<int, string>>();
                using (var st = entry.Open())
                {
                    var data = XDocument.Load(st).Root.Element(Ns + "sheetData");
                    if (data != null)
                    {
                        int autoRow = 0;
                        foreach (var row in data.Elements(Ns + "row"))
                        {
                            int rIdx = int.TryParse((string)row.Attribute("r"), out var rr) ? rr - 1 : autoRow;
                            autoRow = rIdx + 1;
                            var cells = new Dictionary<int, string>();
                            int autoCol = 0;
                            foreach (var c in row.Elements(Ns + "c"))
                            {
                                string refStr = (string)c.Attribute("r");
                                int col = refStr != null ? ColIndex(refStr) : autoCol;
                                autoCol = col + 1;
                                cells[col] = CellValue(c, shared);
                            }
                            rows[rIdx] = cells;
                        }
                    }
                }

                var list = new List<string[]>();
                if (rows.Count > 0)
                {
                    int maxRow = rows.Keys.Max();
                    int maxCol = rows.Values.Where(r => r.Count > 0).Select(r => r.Keys.Max()).DefaultIfEmpty(-1).Max() + 1;
                    for (int r = 0; r <= maxRow; r++)
                    {
                        var arr = new string[maxCol];
                        if (rows.TryGetValue(r, out var cells))
                            foreach (var kv in cells) arr[kv.Key] = kv.Value;
                        list.Add(arr);
                    }
                }
                result[name] = list;
            }
            }
            return result;
        }

        static string CellValue(XElement c, List<string> shared)
        {
            string t = (string)c.Attribute("t");
            string v = (string)c.Element(Ns + "v");
            switch (t)
            {
                case "s": return int.TryParse(v, out int i) && i >= 0 && i < shared.Count ? shared[i] : "";
                case "inlineStr": return string.Concat(c.Descendants(Ns + "t").Select(x => x.Value));
                case "b": return v == "1" ? "TRUE" : "FALSE";
                case "str":
                case "e": return v ?? "";
                default:
                    if (v == null) return "";
                    // 숫자: 0.70999999999 같은 부동소수 오차 정리
                    return double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double d)
                        ? d.ToString("0.############", CultureInfo.InvariantCulture) : v;
            }
        }

        static int ColIndex(string cellRef)
        {
            int col = 0;
            foreach (char ch in cellRef)
            {
                if (ch < 'A' || ch > 'Z') break;
                col = col * 26 + (ch - 'A' + 1);
            }
            return col - 1;
        }
    }
}
