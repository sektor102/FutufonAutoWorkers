"""Verify reminder assumptions using a local extraction of the installed game."""
import json
import struct
import sys
from pathlib import Path

data = json.loads(Path(sys.argv[1]).read_text(encoding="utf-8"))
def fsm(name):
    return next(c["tree"]["fsm"] for c in data if c["owner_name"] == "FACTORY" and c["tree"].get("fsm", {}).get("name") == name)

def numeric_actions(f, state_name, kind):
    d = next(s["actionData"] for s in f["states"] if s["name"] == state_name)
    for i, action_name in enumerate(d["actionNames"]):
        if action_name.rsplit(".", 1)[-1] != kind: continue
        values = {}
        end = d["actionStartIndex"][i + 1] if i + 1 < len(d["actionNames"]) else len(d["paramName"])
        for p in range(d["actionStartIndex"][i], end):
            t, pos, size = d["paramDataType"][p], d["paramDataPos"][p], d["paramByteDataSize"][p]
            b = bytes(d["byteData"][pos:pos + size])
            if t in (15, 16, 17):
                width = 1 if t == 17 else 4
                value = bool(b[0]) if t == 17 else struct.unpack("<f" if t == 15 else "<i", b[:4])[0]
                if len(b) > width and b[width]: value = "$" + b[width + 1:].decode()
                values[d["paramName"][p]] = value
        yield values

database = fsm("Database")
times = [a["float2"] for a in numeric_actions(database, "Factory open", "FloatCompare")]
assert 11.0 in times and 16.0 in times, "Installed game shift times changed"
assert next(numeric_actions(database, "Lunch time", "SetIntValue")) == {"intVariable": "$Lunchbreak", "intValue": 1}
assert next(numeric_actions(database, "Home time", "SetBoolValue")) == {"boolVariable": "$Home", "boolValue": True}
assert "TimeHourF" in [v["name"] for v in fsm("Clock")["variables"]["floatVariables"]]
assert "AtWork" in [v["name"] for v in fsm("PlayerData")["variables"]["boolVariables"]]
player = fsm("PlayerData")
assert {"PunchInMinutes", "PunchOutMinutes", "WorkMinutesDayF", "WorkMinutesOvertimeF"} <= {v["name"] for v in player["variables"]["floatVariables"]}
assert "DayActive" in {v["name"] for v in player["variables"]["boolVariables"]}
# Native elapsed minutes are checkout minus check-in. Those saved values recover
# checkout after reload, while PunchOutMinutes itself is not saved by the game.
subtract = next(numeric_actions(player, "Time bank F", "FloatOperator"))
assert subtract == {"float1":"$PunchOutMinutes", "float2":"$PunchInMinutes", "storeResult":"$WorkMinutesDayF"}
load_vars = {a["loadValue"] for a in numeric_actions(player, "Load game", "LoadFloat")}
assert {"$PunchInMinutes", "$WorkMinutesDayF", "$WorkMinutesOvertimeF"} <= load_vars
assert "$PunchOutMinutes" not in load_vars
tuesday = next(numeric_actions(database, "Tuesday", "IntCompare"))
assert tuesday == {"integer1":"$GlobalDay", "integer2":2}
print("PASS: native clock/attendance, lunch at 11, shift finish at 16, Tuesday mapping, punch times, saved last shift and native overtime balance.")
