# 2026-09-10 运行结果摘录

以下内容从本地实际运行日志提取；机器绝对路径替换为 <PROJECT>。

## core.log

```text
PASS nine modules and twelve resources
PASS unknown fields rejected
PASS numeric strings rejected
PASS duplicate JSON keys rejected
PASS duplicate IDs rejected
PASS missing references rejected
PASS wrong reference types rejected
PASS independent material loading
PASS independent pool indexing but missing execution dependencies
PASS query deep-copy isolation
PASS source mutation isolation
PASS legitimate reciprocal character-board references
PASS talent prerequisite cycles rejected
PASS unsupported craft probability rejected
PASS negative zero-sum and overflow weights rejected
PASS pity without positive target rejected
PASS bad hot reload preserves old snapshot
PASS unsupported capability rejected
PASS collection format missing numeric and unknown rejected
PASS unsupported inventory quality and oversized instances rejected
PASS valid reload switches new operations only
PASS concurrent hot reload cannot delete newly published IDs
PASS all nine CSV tables match JSON source
PASS CSV BOM escaped quotes commas multiline CRLF
PASS CSV malformed quotes rejected
PASS CSV duplicate unknown missing headers rejected
PASS CSV leading-zero integers rejected
PASS CSV nested unknown fields rejected
PASS character construction and instance isolation
PASS all 1000 weighted intervals
PASS entry physical order does not affect stable ID order
PASS missing reward handler rejects before fee and RNG
PASS retained candidate cannot mutate committed state
PASS nested transactions reject and release transaction guard
PASS concurrent retries share a single economic result
PASS draw atomic cost reward pity and replay
PASS insufficient draw funds leave state untouched
PASS reward overflow rolls back fee and pity
PASS pity threshold and natural reset
PASS craft cost time boundary idempotency
PASS insufficient material never partially pays
PASS duplicate input costs aggregate
PASS started craft retains output across reload
PASS state snapshots are detached
PASS Unity metadata exists and all serialized GUIDs resolve
PASS nine committed SO payloads match source JSON
PASS assembly definition references resolve
RESULT 47 checks passed; real C# sources compiled with C# 7.3.
```

## import.log

```text
1. SO -> Runtime: demo-001, 9 modules, 12 resources.
2. Character: character.apollo; equipment=1; abilities=1; talents=talent_board.apollo; perfume=1
3. Gacha: material.herb x5; ticket=1; repeated operation charged once.
4. Craft at 29s: rejected; no output granted.
5. JSON -> transient SO -> Runtime hot reload: old job grants 1; new job grants 2; total potion=3.
6. Invalid hotfix rejected; current version preserved. Reason: Resource/dependency not loaded: material.missing
PASS: herb=9, water=3, potion=3, ticket=1, actionPoints=6.
```

## playmode.log

```text
[Play Mode Validation] PASS: DemoScene Start executed successfully in Play Mode.
```

## build.log

```text
[Batch Build] PASS: <PROJECT>\Builds\Windows\ResourceFrameworkDemo.exe (96557882 bytes).
```

## player.log

```text
1. SO -> Runtime: demo-001, 9 modules, 12 resources.
2. Character: character.apollo; equipment=1; abilities=1; talents=talent_board.apollo; perfume=1
3. Gacha: material.herb x5; ticket=1; repeated operation charged once.
4. Craft at 29s: rejected; no output granted.
5. JSON -> transient SO -> Runtime hot reload: old job grants 1; new job grants 2; total potion=3.
6. Invalid hotfix rejected; current version preserved. Reason: Resource/dependency not loaded: material.missing
PASS: herb=9, water=3, potion=3, ticket=1, actionPoints=6.
```
