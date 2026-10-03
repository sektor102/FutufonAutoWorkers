import json
import struct
import sys
from pathlib import Path

# The input is a local extraction from the installed game, not bundled game data.
asset_path = Path(sys.argv[1]) if len(sys.argv) > 1 else Path(__file__).parents[2] / 'tools' / 'factory-asset-data.json'
data = json.loads(asset_path.read_text(encoding='utf-8'))
def fsm(owner, name='Use'):
    return next(c['tree']['fsm'] for c in data if c['owner_name']==owner and c['tree'].get('fsm',{}).get('name')==name)
def state(f, name):
    return next(s for s in f['states'] if s['name']==name)
def actions(s):
    d=s['actionData']
    for i,full_name in enumerate(d['actionNames']):
        fields={}
        start=d['actionStartIndex'][i]
        end=d['actionStartIndex'][i+1] if i+1<len(d['actionNames']) else len(d['paramName'])
        for p in range(start,end):
            t,pos,size=d['paramDataType'][p],d['paramDataPos'][p],d['paramByteDataSize'][p]
            b=bytes(d['byteData'][pos:pos+size])
            if t in (15,16,17):
                width=1 if t==17 else 4
                literal=bool(b[0]) if t==17 else struct.unpack('<f' if t==15 else '<i',b[:4])[0]
                value='$'+b[width+1:].decode() if len(b)>width and b[width] else literal
            elif t in (18,19,20):
                v=d[{18:'fsmStringParams',19:'fsmGameObjectParams',20:'fsmOwnerDefaultParams'}[t]][pos]
                if t==20: v=v['gameObject']
                value='$'+v['name'] if v['useVariable'] else v.get('value')
            elif t==23: value=b.decode()
            else: value=None
            fields[d['paramName'][p]]=value
        yield full_name.rsplit('.',1)[-1], fields
def action(f, s, kind):
    return next(fields for name,fields in actions(state(f,s)) if name==kind)

for source,container,pick,stock in [('PickChargers','chargers box(Clone)','Pick item',40),('PickManuals','manuals box(Clone)','Pick item',80),('PickTrays','plastic trays(Clone)','State 8',20),('PickSheets','packaging sheets(Clone)','State 8',20)]:
    assert action(fsm(source),'Check old','IsActive')['isNotActiveEvent']=='PROCEED'
    use=fsm(container)
    assert use['startState']=='State 3' and use['RestartOnEnable']
    assert action(use,'State 3','SetIntValue')['intValue']==stock
    assert any(name=='CreateObject' for name,_ in actions(state(use,pick)))
    decrement=action(use,pick,'IntAdd')
    assert decrement['intVariable']=='$Items' and decrement['add']==-1

for trigger,flag,owner in [('TriggerCharger','Assembled','Mould'),('TriggerManual','Assembled','Mould'),('TriggerTray','InstalledMould','ThisPackage')]:
    f=fsm(trigger,'Assembly')
    write=action(f,'Assemble','SetBoolValue')
    assert write['boolVariable']=='$'+flag and write['boolValue'] is True
    parent=action(f,'State 2','SetParent')
    assert parent['gameObject']=='$Part' and parent['resetLocalPosition'] and parent['resetLocalRotation']
    assert action(f,'State 2','SendEventByName')['sendEvent']=='ASSEMBLE'
    assert owner in [v['name'] for v in f['variables']['gameObjectVariables']]
    assert ('FINISHED','State 2') in [(t['fsmEvent']['name'],t['toState']) for t in state(f,'Assemble')['transitions']]
    assert ('FINISHED','State 1') in [(t['fsmEvent']['name'],t['toState']) for t in state(f,'State 2')['transitions']]

package=fsm('package(Clone)')
assert next(v['value'] for v in package['variables']['intVariables'] if v['name']=='Stage')==0
assert action(package,'State 1','IntAdd')['add']==1
assert [a['integer2'] for name,a in actions(state(package,'State 1')) if name=='IntCompare']==[1,2,3,4,5,6]
assert action(package,'State 7','SetIntValue')['intValue']==4
assert action(fsm('TriggerCharger','Assembly'),'State 1','SetFsmBool')['variableName']=='Chager'
assert {a['variableName'] for name,a in actions(state(fsm('TriggerTray','Assembly'),'State 1')) if name=='SetFsmBool'}=={'Mould','Charger','Manual'}
shipping = fsm('TriggerPackage','Assembly')
assert shipping['RestartOnEnable'] and shipping['startState']=='State 5'
assert action(shipping,'Check full','IntCompare')['integer2']==44
assert action(fsm('PickBoxes'),'Check old','IsActive')['isNotActiveEvent']=='PROCEED'
assert action(fsm('PickBoxes'),'Pick BOX','ActivateGameObject')['gameObject']=='$Package'
assert {a['variableName'] for name,a in actions(state(shipping,'Assemble')) if name=='GetFsmBool'}=={'Charger','Manual','Mould'}
assert action(shipping,'Assemble','BoolAllTrue')['storeResult']=='$ContentOK'
assert action(shipping,'Check contents','DestroyObject')['gameObject']=='$Part'
assert action(shipping,'Check contents','BoolTest')['isTrue']=='PROCEED'
assert {a['intVariable'] for name,a in actions(state(shipping,'Reset data')) if name=='SetIntValue' and a['intValue']==0}=={'$Slot','$EmptyPackages','$TotalPackages'}

def walk(f, entry, values, terminals):
    """Execute the relevant serialized counter/branch actions in the native graph.

    Rendering, audio and physics are not simulated. The Wait event is delivered
    immediately. ContentOK is supplied as an input from the contents checks.
    """
    current=entry
    def value(v):
        return values[v[1:]] if isinstance(v,str) and v.startswith('$') else v
    for _ in range(50):
        if current in terminals:
            return current
        s=state(f,current)
        transitions={t['fsmEvent']['name']:t['toState'] for t in s['transitions']}
        event='FINISHED'
        for kind,a in actions(s):
            emitted=None
            if kind=='IntAdd': values[a['intVariable'][1:]]+=value(a['add'])
            elif kind=='SetIntValue': values[a['intVariable'][1:]]=value(a['intValue'])
            elif kind=='IntCompare':
                left,right=value(a['integer1']),value(a['integer2'])
                emitted=a['equal' if left==right else 'lessThan' if left<right else 'greaterThan']
            elif kind=='BoolTest': emitted=a['isTrue' if value(a['boolVariable']) else 'isFalse']
            elif kind=='Wait': emitted=a['finishEvent']
            if emitted and emitted in transitions:
                event=emitted
                break
        assert event in transitions, (current,event)
        current=transitions[event]
    raise AssertionError('Native state loop did not settle')

for initial in (0,4,43):
    counts={'TotalPackages':initial,'EmptyPackages':0,'Slot':initial%4,'ContentOK':True}
    for total in range(initial+1,45):
        settled=walk(shipping,'Assemble',counts,{'Check package','Close box'})
        assert counts['TotalPackages']==total and counts['EmptyPackages']==0 and counts['Slot']==total%4
        assert settled==('Close box' if total==44 else 'Check package')
bad={'TotalPackages':0,'EmptyPackages':0,'Slot':0,'ContentOK':False}
walk(shipping,'Assemble',bad,{'Check package','Close box'})
assert bad['EmptyPackages']==1, 'Missing components really are classified as defective by the game'

pallets=[c['tree']['fsm'] for c in data if c['owner_name']=='TriggerBox' and c['tree'].get('fsm',{}).get('name')=='Assembly']
assert len(pallets)==3
for pallet in pallets:
    assert action(pallet,'Insert box','IntCompare')['integer2']==4
    assert action(pallet,'Assemble','IntAdd')=={'intVariable':'$Slot','add':1,'everyFrame':None}
    copied={a['variableName']:a['storeValue'] for name,a in actions(state(pallet,'Insert box')) if name=='GetFsmInt'}
    assert copied=={'EmptyPackages':'$PackagesEmpty','TotalPackages':'$PackagesTotal'}
    credited={a['variableName']:a['add'] for name,a in actions(state(pallet,'Insert box')) if name=='AddToFsmInt'}
    assert credited=={'PackagesEmpty':'$PackagesEmpty','PackagesTotal':'$PackagesTotal'}
    visibility={a['gameObject']:a['activate'] for name,a in actions(state(pallet,'Insert box')) if name=='ActivateGameObject'}
    assert visibility=={'$CurrentSlot':True,'$Part':False}
    for slot in range(4):
        counts={'Slot':slot}
        final=walk(pallet,'Assemble',counts,{'Check for Part collision','State 2'})
        assert counts['Slot']==slot+1 and final==('State 2' if slot==3 else 'Check for Part collision')
for proxy in [c['tree'] for c in data if c['owner_name']=='TriggerBox' and c['tree'].get('referenceName')=='Slots']:
    assert proxy['preFillCount']==5 and proxy['preFillGameObjectList'][0]['m_PathID']==0
    assert all(v['m_PathID'] for v in proxy['preFillGameObjectList'][1:])
supervisor=fsm('CheckSlacking','Logic')
assert action(supervisor,'Boss in?','FloatCompare')['float1']=='$Distance'
assert action(supervisor,'Boss in?','FloatCompare')['float2']=='$Tolerance'
assert action(supervisor,'Lunch and work','GetFsmBool')['variableName']=='AtWork'
assert action(supervisor,'Lunch and work','IntCompare')['integer2']==11
assert action(supervisor,'Is slacking','AddFsmFloat')['variableName']=='SlackMinutesAllTime'
assert abs(action(supervisor,'Is slacking','AddFsmFloat')['addValue']-0.2)<0.00001
assert action(supervisor,'Is slacking','Wait')['time']==2
assert action(supervisor,'Is working','Wait')['time']==55
assert action(supervisor,'Reset state','SetBoolValue')['boolVariable']=='$IsWorking'
assert action(supervisor,'Reset state','SetBoolValue')['boolValue'] is False
print('PASS: native consumption/refill, assembly and closure; 44-package packing including resume at 4/43, defect detection, box reset, player pallets/job accounting and supervisor pulse reset/idle counter semantics.')
