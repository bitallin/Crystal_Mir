#!/usr/bin/env python3
import json
import sys
import urllib.error
import urllib.request

if len(sys.argv) != 5:
    raise SystemExit("usage: test-m2-content.py <api-base> <draft-id> <access-token> <import-run-id>")

base, draft_id, token, import_run_id = sys.argv[1:5]
draft_id = int(draft_id)
import_run_id = int(import_run_id)

EDITORS = ["item", "monster", "map", "npc", "quest", "magic", "gameshop", "recipe", "script", "drop"]


class ApiError(AssertionError):
    def __init__(self, method, path, status, payload):
        super().__init__(f"{method} {path} returned {status}: {payload}")
        self.status = status
        self.payload = payload


def request(method, path, body=None, auth=True, raw=None, content_type="application/json"):
    data = None
    if raw is not None:
        data = raw.encode("utf-8")
    elif body is not None:
        data = json.dumps(body, ensure_ascii=False).encode("utf-8")
    req = urllib.request.Request(base + path, data=data, method=method)
    req.add_header("Accept", "application/json")
    if data is not None:
        req.add_header("Content-Type", content_type)
    if auth:
        req.add_header("Authorization", f"Bearer {token}")
    try:
        with urllib.request.urlopen(req, timeout=120) as response:
            text = response.read().decode("utf-8")
            try:
                payload = json.loads(text) if text else None
            except json.JSONDecodeError:
                payload = None
            return response.status, payload, text
    except urllib.error.HTTPError as error:
        text = error.read().decode("utf-8")
        try:
            payload = json.loads(text) if text else None
        except json.JSONDecodeError:
            payload = text
        return error.code, payload, text


def expect(method, path, status, body=None, auth=True, raw=None, content_type="application/json"):
    actual, payload, text = request(method, path, body=body, auth=auth, raw=raw, content_type=content_type)
    if actual != status:
        raise ApiError(method, path, actual, payload)
    return payload if payload is not None else text


def default_value(field):
    kind = field["kind"]
    if kind == "boolean":
        return False
    if kind in ("integer", "number"):
        return 0
    if kind in ("string", "code"):
        return ""
    if kind == "enum":
        options = field.get("options") or []
        return options[0]["value"] if options else ""
    if kind == "flags":
        return "None"
    if kind in ("array", "reference-array", "string-array", "integer-array", "drop-table"):
        return []
    if kind == "stat-map":
        return {"Values": {}}
    if kind == "color":
        return {"a": 255, "r": 255, "g": 255, "b": 255}
    if kind == "reference":
        if field.get("storeAs") == "string":
            return ""
        if field.get("storeAs") == "ref":
            return None
        return 0
    return field.get("defaultValue")


def default_data(schema):
    data = {}

    def visit(fields, target):
        for field in fields:
            path = field["path"]
            if field["kind"] == "object" and field.get("fields"):
                child = {}
                target[path] = child
                visit(field["fields"], child)
            else:
                target[path] = default_value(field)

    for section in schema.get("sections", []):
        visit(section.get("fields", []), data)
    return data


def create_data(entity_type, schema):
    data = default_data(schema)
    suffix = f"M2Smoke{draft_id}"
    if entity_type == "item":
        data["Name"] = suffix
    elif entity_type == "monster":
        data["Name"] = suffix
    elif entity_type == "map":
        data["FileName"] = suffix
        data["Title"] = suffix
    elif entity_type == "npc":
        options = expect("GET", f"/api/v1/content/script/options?draftId={draft_id}&limit=1", 200)["items"]
        script = options[0]
        data["Name"] = suffix
        data["MapIndex"] = 1
        data["Location"] = {"x": 1, "y": 1}
        data["FileName"] = script.get("summary") or script.get("label") or ""
    elif entity_type == "quest":
        data["Name"] = suffix
        data["NpcIndex"] = 1
        data["FinishNpcIndex"] = 1
    elif entity_type == "magic":
        used = {
            item.get("data", {}).get("Spell")
            for item in expect("GET", "/api/v1/content/magic?pageSize=200", 200)["items"]
        }
        spell_field = next(
            field for section in schema["sections"] for field in section["fields"] if field["path"] == "Spell"
        )
        choices = [option["value"] for option in spell_field.get("options", []) if option["value"] != "None"]
        spell = next((choice for choice in choices if choice not in used), None)
        assert spell is not None, "No unused magic Spell remains for the typed create test."
        data["Name"] = suffix
        data["Spell"] = spell
    elif entity_type == "gameshop":
        data["ItemIndex"] = 1
        data["Info"] = {"$ref": "item", "key": "item.1", "index": 1}
        data["GoldPrice"] = 100
        data["CreditPrice"] = 0
        data["Count"] = 1
        data["CanBuyGold"] = True
    elif entity_type == "recipe":
        data["name"] = suffix
        data["output"] = {
            "itemIndex": 1,
            "itemName": suffix,
            "count": 1,
            "currentDura": 0,
            "maxDura": 0,
        }
        data["ingredients"] = []
        data["tools"] = []
        data["requiredClasses"] = []
        data["requiredFlags"] = []
        data["requiredQuests"] = []
        data["chance"] = 100
        data["gold"] = 0
    elif entity_type == "script":
        data["relativePath"] = f"M2/Smoke{draft_id}/Script.txt"
        data["language"] = "CrystalNpc"
        data["source"] = "[@MAIN]\nSAY M2 smoke\n"
    elif entity_type == "drop":
        data["monsterIndex"] = 880000 + draft_id
        data["monsterName"] = suffix
        data["dropPath"] = suffix
        data["drops"] = []
    return data


UPDATE_FIELD = {
    "item": ("Name", "M2UpdatedItem"),
    "monster": ("Name", "M2UpdatedMonster"),
    "map": ("Title", "M2UpdatedMap"),
    "npc": ("Name", "M2UpdatedNpc"),
    "quest": ("Name", "M2UpdatedQuest"),
    "magic": ("Name", "M2UpdatedMagic"),
    "gameshop": ("Category", "M2Smoke"),
    "recipe": ("chance", 99),
    "script": ("source", "[@MAIN]\nSAY M2 updated\n"),
    "drop": ("monsterName", "M2UpdatedDrop"),
}


def make_invalid(entity_type, data):
    value = json.loads(json.dumps(data))
    if entity_type == "item":
        value["Name"] = 123
    elif entity_type == "monster":
        value["AI"] = "fast"
    elif entity_type == "map":
        value["Light"] = "Noon"
    elif entity_type == "npc":
        value["Location"] = {"x": "left", "y": 1}
    elif entity_type == "quest":
        value["RequiredMinLevel"] = "high"
    elif entity_type == "magic":
        value["BaseCost"] = "free"
    elif entity_type == "gameshop":
        value["Count"] = 0
    elif entity_type == "recipe":
        value["chance"] = 101
    elif entity_type == "script":
        value["source"] = 123
    elif entity_type == "drop":
        value["monsterName"] = 123
    return value


def typed_crud(entity_type, schema):
    created = expect(
        "POST",
        f"/api/v1/drafts/{draft_id}/content/{entity_type}",
        201,
        {"schemaVersion": schema["schemaVersion"], "reason": f"M2 {entity_type} create", "data": create_data(entity_type, schema)},
    )
    index = created["localIndex"]
    key = created["contentKey"]
    assert created["revision"] == 1, created

    invalid = make_invalid(entity_type, created["data"])
    expect(
        "PUT",
        f"/api/v1/drafts/{draft_id}/content/{entity_type}/{index}",
        400,
        {"contentKey": key, "schemaVersion": 1, "expectedRevision": 1, "reason": "M2 invalid structure", "data": invalid},
    )

    updated_data = json.loads(json.dumps(created["data"]))
    field, value = UPDATE_FIELD[entity_type]
    updated_data[field] = value
    updated = expect(
        "PUT",
        f"/api/v1/drafts/{draft_id}/content/{entity_type}/{index}",
        200,
        {"contentKey": key, "schemaVersion": 1, "expectedRevision": 1, "reason": f"M2 {entity_type} update", "data": updated_data},
    )
    assert updated["revision"] == 2, updated

    expect(
        "PUT",
        f"/api/v1/drafts/{draft_id}/content/{entity_type}/{index}",
        409,
        {"contentKey": key, "schemaVersion": 1, "expectedRevision": 1, "reason": "M2 stale", "data": updated_data},
    )
    expect(
        "DELETE",
        f"/api/v1/drafts/{draft_id}/content/{entity_type}/{index}",
        204,
        {"expectedRevision": 2, "reason": f"M2 {entity_type} delete"},
    )


def test_schemas():
    for entity_type in EDITORS:
        schema = expect("GET", f"/api/v1/editor-schemas/{entity_type}", 200)
        assert schema["entityType"] == entity_type, schema
        expect("GET", f"/api/v1/editor-schemas/{entity_type}", 401, auth=False)


def test_options():
    payload = expect("GET", f"/api/v1/content/item/options?draftId={draft_id}&q=Spirit&limit=5", 200)
    assert payload["entityType"] == "item", payload
    assert 1 <= len(payload["items"]) <= 5, payload
    assert all(item["contentKey"].startswith("item.") for item in payload["items"]), payload
    script_options = expect("GET", f"/api/v1/content/script/options?draftId={draft_id}&q=Transport&limit=5", 200)
    assert script_options["items"], script_options


def test_import_export():
    exported = expect("GET", f"/api/v1/drafts/{draft_id}/content/item/export", 200)
    raw = exported if isinstance(exported, str) else ""
    lines = [json.loads(line) for line in raw.splitlines() if line.strip()]
    assert lines, "Draft item export must not be empty."
    assert [line["localIndex"] for line in lines] == sorted(line["localIndex"] for line in lines), \
        "Draft export must be stably sorted by localIndex."
    assert all(line["entityType"] == "item" for line in lines), lines[0]

    run_export = expect("GET", f"/api/v1/drafts/{draft_id}/content/item/export?importRunId={import_run_id}", 200)
    run_lines = [json.loads(line) for line in run_export.splitlines() if line.strip()]
    assert run_lines and all(line["entityType"] == "item" for line in run_lines), "Import-run export must return snapshot JSONL."

    source = expect("GET", f"/api/v1/drafts/{draft_id}/content/item/1", 200)
    high = 9000000 + draft_id
    data = json.loads(json.dumps(source["data"]))
    data["Index"] = high
    data["Name"] = "M2JsonlItem"
    line = {"entityType": "item", "localIndex": high, "contentKey": f"item.{high}", "schemaVersion": 1, "data": data}
    raw = json.dumps(line, ensure_ascii=False, separators=(",", ":")) + "\n"

    dry = expect(
        "POST",
        f"/api/v1/drafts/{draft_id}/content/item/import?dryRun=true",
        200,
        raw=raw,
        content_type="application/x-ndjson",
    )
    assert dry["valid"] is True and dry["validLines"] == 1 and dry["appliedLines"] == 0, dry

    applied = expect(
        "POST",
        f"/api/v1/drafts/{draft_id}/content/item/import?dryRun=false&reason=M2%20JSONL",
        200,
        raw=raw,
        content_type="application/x-ndjson",
    )
    assert applied["valid"] is True and applied["appliedLines"] == 1, applied
    inserted = expect("GET", f"/api/v1/drafts/{draft_id}/content/item/{high}", 200)
    assert inserted["data"]["Name"] == "M2JsonlItem", inserted
    expect(
        "DELETE",
        f"/api/v1/drafts/{draft_id}/content/item/{high}",
        204,
        {"expectedRevision": 1, "reason": "M2 JSONL cleanup"},
    )

    duplicate = raw + raw
    duplicate_dry = expect(
        "POST",
        f"/api/v1/drafts/{draft_id}/content/item/import?dryRun=true",
        200,
        raw=duplicate,
        content_type="application/x-ndjson",
    )
    assert duplicate_dry["valid"] is False, duplicate_dry
    assert any(error["code"] == "item-duplicate" for error in duplicate_dry["errors"]), duplicate_dry
    expect(
        "POST",
        f"/api/v1/drafts/{draft_id}/content/item/import?dryRun=false",
        400,
        raw=duplicate,
        content_type="application/x-ndjson",
    )

    invalid = expect(
        "POST",
        f"/api/v1/drafts/{draft_id}/content/item/import?dryRun=true",
        200,
        raw="{not-json\n",
        content_type="application/x-ndjson",
    )
    assert invalid["valid"] is False and invalid["errors"], invalid

    mismatched = json.loads(json.dumps(line))
    mismatched["contentKey"] = f"item.{high + 1}"
    expect(
        "POST",
        f"/api/v1/drafts/{draft_id}/content/item/import?dryRun=false",
        400,
        raw=json.dumps(mismatched, separators=(",", ":")) + "\n",
        content_type="application/x-ndjson",
    )


def test_script_validation():
    report = expect(
        "POST",
        f"/api/v1/drafts/{draft_id}/content/script/0/validate",
        200,
        {"source": "[@MAIN]\nSAY M2 validation\n\n[@NEXT]\nSAY Done\n", "language": "CrystalNpc"},
    )
    assert report["valid"] is True, report
    assert report["pages"] == ["@MAIN", "@NEXT"], report
    assert len(report["sha256"]) == 64 and len(report["compiledSha256"]) == 64, report
    assert report["ast"]["pages"], report
    expect("POST", f"/api/v1/drafts/{draft_id}/content/script/0/validate", 401, body={"source": "[@MAIN]\n"}, auth=False)


def test_completeness_and_references():
    missing = expect(
        "POST",
        f"/api/v1/drafts/{draft_id}/content/drop",
        201,
        {
            "schemaVersion": 1,
            "reason": "M2 missing references",
            "data": {
                "monsterIndex": 987654,
                "monsterName": "MissingMonster",
                "dropPath": "MissingMonster",
                "drops": [
                    {
                        "Item": {"$ref": "item", "key": "item.999999", "index": 999999},
                        "Type": 0,
                        "Chance": 1,
                        "QuestRequired": False,
                    }
                ],
            },
        },
    )
    incomplete = expect(
        "POST",
        f"/api/v1/drafts/{draft_id}/content/item",
        201,
        {"schemaVersion": 1, "reason": "M2 incomplete item", "data": {"Name": ""}},
    )
    report = expect("POST", f"/api/v1/drafts/{draft_id}/validate", 200)
    assert report["valid"] is False, report
    codes = {(issue["code"], issue["contentKey"]) for issue in report["issues"]}
    assert ("reference-missing", missing["contentKey"]) in codes, report["issues"]
    assert any(code == "item-completeness" for code, _ in codes), report["issues"]
    expect(
        "DELETE",
        f"/api/v1/drafts/{draft_id}/content/drop/{missing['localIndex']}",
        204,
        {"expectedRevision": 1, "reason": "M2 missing reference cleanup"},
    )
    expect(
        "DELETE",
        f"/api/v1/drafts/{draft_id}/content/item/{incomplete['localIndex']}",
        204,
        {"expectedRevision": 1, "reason": "M2 incomplete cleanup"},
    )


def put_map_array(map_index, revision, group, items):
    result = expect(
        "PUT",
        f"/api/v1/drafts/{draft_id}/content/map/{map_index}/{group}",
        200,
        {"expectedRevision": revision, "reason": f"M2 map {group}", "items": items},
    )
    return result["revision"]


def save_relation_entity(map_index, entity_type, index, revision, data, create=False):
    path = f"/api/v1/drafts/{draft_id}/content/map/{map_index}/{'npcs' if entity_type == 'npc' else 'respawns'}"
    if index is not None:
        path += f"/{index}"
    return expect(
        "POST" if create else "PUT" if index is not None else "POST",
        path,
        201 if create else 200,
        {"expectedRevision": revision, "reason": f"M2 {entity_type} row", "data": data},
    )


def test_map_relations():
    map_schema = expect("GET", "/api/v1/editor-schemas/map", 200)
    map_data = create_data("map", map_schema)
    map_data["FileName"] = f"M2Relations{draft_id}"
    map_data["Title"] = f"M2Relations{draft_id}"
    relation_map = expect(
        "POST",
        f"/api/v1/drafts/{draft_id}/content/map",
        201,
        {"schemaVersion": map_schema["schemaVersion"], "reason": "M2 relation map", "data": map_data},
    )
    map_index = relation_map["localIndex"]

    relations = expect("GET", f"/api/v1/drafts/{draft_id}/content/map/{map_index}/relations", 200)
    original = {
        "movements": relations["movements"],
        "safezones": relations["safeZones"],
        "minezones": relations["mineZones"],
        "activecoords": relations["activeCoords"],
    }
    revision = relations["revision"]

    movement = {
        "MapIndex": 1,
        "Source": {"x": 1, "y": 1},
        "Destination": {"x": 1, "y": 1},
        "NeedHole": False,
        "NeedMove": False,
        "ShowOnBigMap": True,
        "ConquestIndex": 0,
        "Icon": 0,
    }
    old_revision = revision
    revision = put_map_array(map_index, revision, "movements", [movement])
    assert revision == old_revision + 1, (old_revision, revision)
    expect(
        "PUT",
        f"/api/v1/drafts/{draft_id}/content/map/{map_index}/movements",
        409,
        {"expectedRevision": old_revision, "reason": "M2 stale map", "items": [movement]},
    )
    revision = put_map_array(map_index, revision, "safezones", [{"Location": {"x": 1, "y": 1}, "Size": 1, "StartPoint": True}])
    revision = put_map_array(map_index, revision, "minezones", [{"Location": {"x": 1, "y": 1}, "Size": 1, "Mine": 0}])
    revision = put_map_array(map_index, revision, "activecoords", [{"x": 1, "y": 1}])

    respawn = save_relation_entity(
        map_index,
        "respawn",
        None,
        revision,
        {
            "MonsterIndex": 1,
            "Location": {"x": 1, "y": 1},
            "Count": 1,
            "Spread": 1,
            "Delay": 1,
            "RandomDelay": 0,
            "Direction": 0,
            "RoutePath": "",
            "RespawnTicks": 0,
            "SaveRespawnTime": False,
        },
        create=True,
    )
    assert respawn["revision"] == 1, respawn
    respawn_data = json.loads(json.dumps(respawn["data"]))
    respawn_data["Count"] = 2
    updated = save_relation_entity(map_index, "respawn", respawn["localIndex"], 1, respawn_data)
    assert updated["revision"] == 2, updated
    expect(
        "PUT",
        f"/api/v1/drafts/{draft_id}/content/map/{map_index}/respawns/{respawn['localIndex']}",
        409,
        {"expectedRevision": 1, "reason": "M2 stale respawn", "data": respawn_data},
    )
    expect(
        "DELETE",
        f"/api/v1/drafts/{draft_id}/content/map/{map_index}/respawns/{respawn['localIndex']}",
        204,
        {"expectedRevision": 2, "reason": "M2 respawn cleanup"},
    )

    scripts = expect("GET", f"/api/v1/content/script/options?draftId={draft_id}&limit=1", 200)["items"]
    script_name = scripts[0].get("summary") or scripts[0].get("label") or ""
    assert script_name, scripts
    current_map = expect("GET", f"/api/v1/drafts/{draft_id}/content/map/{map_index}", 200)
    npc = save_relation_entity(
        map_index,
        "npc",
        None,
        current_map["revision"],
        {
            "Name": "M2SmokeNpc",
            "FileName": script_name,
            "MapIndex": map_index,
            "Location": {"x": 1, "y": 1},
            "Rate": 100,
            "Image": 0,
            "ShowOnBigMap": False,
            "TimeVisible": False,
        },
        create=True,
    )
    assert npc["revision"] == 1, npc
    npc_data = json.loads(json.dumps(npc["data"]))
    npc_data["Name"] = "M2SmokeNpcUpdated"
    npc_data["Location"] = {"x": 2, "y": 2}
    updated_npc = save_relation_entity(map_index, "npc", npc["localIndex"], 1, npc_data)
    assert updated_npc["revision"] == 2, updated_npc
    expect(
        "PUT",
        f"/api/v1/drafts/{draft_id}/content/map/{map_index}/npcs/{npc['localIndex']}",
        409,
        {"expectedRevision": 1, "reason": "M2 stale npc", "data": npc_data},
    )
    expect(
        "DELETE",
        f"/api/v1/drafts/{draft_id}/content/map/{map_index}/npcs/{npc['localIndex']}",
        204,
        {"expectedRevision": 2, "reason": "M2 npc cleanup"},
    )

    current_map = expect("GET", f"/api/v1/drafts/{draft_id}/content/map/{map_index}", 200)
    revision = current_map["revision"]
    for group in ("movements", "safezones", "minezones", "activecoords"):
        revision = put_map_array(map_index, revision, group, original[group])

    restored = expect("GET", f"/api/v1/drafts/{draft_id}/content/map/{map_index}/relations", 200)
    assert restored["movements"] == original["movements"], "Map movement rows were not restored."
    assert restored["safeZones"] == original["safezones"], "Map safe zones were not restored."
    assert restored["mineZones"] == original["minezones"], "Map mine zones were not restored."
    assert restored["activeCoords"] == original["activecoords"], "Map active coordinates were not restored."

    expect(
        "DELETE",
        f"/api/v1/drafts/{draft_id}/content/map/{map_index}",
        204,
        {"expectedRevision": revision, "reason": "M2 relation map cleanup"},
    )


def main():
    before = expect("GET", f"/api/v1/drafts/{draft_id}", 200)
    test_schemas()
    for entity_type in EDITORS:
        schema = expect("GET", f"/api/v1/editor-schemas/{entity_type}", 200)
        typed_crud(entity_type, schema)
    test_options()
    test_import_export()
    test_script_validation()
    test_completeness_and_references()
    test_map_relations()

    after = expect("GET", f"/api/v1/drafts/{draft_id}", 200)
    assert after["entityCount"] == before["entityCount"], (before["entityCount"], after["entityCount"])
    report = expect("POST", f"/api/v1/drafts/{draft_id}/validate", 200)
    assert report["valid"] is True, report
    print(
        "M2 content API verified: 10 schemas with 200/401, typed CRUD + 400/409, options search, "
        "JSONL dry-run/apply/export, script AST, references/completeness, map relation CRUD and cleanup."
    )


if __name__ == "__main__":
    main()
