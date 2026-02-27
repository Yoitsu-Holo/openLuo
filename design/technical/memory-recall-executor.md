# Memory Recall Executor

## Goal

Introduce a stable semantic-memory recall phase before `plan`, without coupling turn orchestration to the current sqlite/embedding implementation.

## Boundary

`MemoryRecallExecutor` is responsible for:

- projecting current turn context into a semantic recall query
- invoking a memory recall service
- formatting recalled memories into prompt-friendly summaries

`MemoryRecallExecutor` is not responsible for:

- embedding generation
- sqlite access
- vector schema
- raw memory storage layout

## Pipeline Position

```text
memoryRecall
-> plan
-> [toolUse -> flowCheck]*
-> charResp
-> statusUpdate
-> memoryCommit
```

This pass only lands `memoryRecall`.

## Core Middle State

Semantic memory is treated as summary-oriented data instead of exact transcript recall.

### MemoryRecord

- source text
- summary
- recall text
- tags
- scope
- emotion
- importance

### SemanticRecallQuery

- search text
- query tags
- scopes
- topK
- recall preference flags

## Interfaces

### Read side

`IMemoryRecallService`

- accepts semantic recall query
- returns semantic memory records plus trace

### Write side

`IMemoryWriteService`

- stores semantic memory records

### Projection side

`IMemoryQueryProjector`

- converts turn input into semantic recall query

`IMemoryWriteProjector`

- converts raw event input into semantic memory record

## Current Implementation

This pass now uses the rebuilt memory stack directly:

- `RuleBasedMemoryQueryProjector`
- `MemoryRecallCoordinator`
- `CompositeMemoryRetriever`
- `DefaultMemoryRecallFormatter`

The legacy memory service and adapters were removed during the refactor.
