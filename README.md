# ANotherRccServiceArbiterLol

ANotherRccServiceArbiterLol (ANRSAL for short) is an arbiter designed to make gameservers and renders with 2008E-2017E RCCService software.

```
--dir "path" | path to ACCService directory, this one is required (DUH!)

lua scripts:
should be obvious, uses --gscript, rscript, rascript, rmscript, rmmscript "path"

server config:
--port "number"
--cores "number"
--baseurl "url"
--name "name" | rccservice name

authentication from arbiter to site:
--secret "key" | api key
--accesskey "key" | gameserver key

misc:
--debug | verbose logging
--experimental | experimental features

example launch: ANRSAL.exe --dir "C:\ACCService" --port 8124 --cores 12 --secret "key" --gscript "C:\ACCService\gameserver.txt" --rscript "C:\ACCService\render.lua" --debug
```

To interact with the arbiter, here are the API endpoints:

`/StartGame?type={GameServer, Avatar, Mesh, Model, Place}`

Place takes `{"PlaceId": 1}`
Avatar takes `{"UserId": 1, "IsHeadshot": false, "IsClothing": false}`
Model takes `{"AssetId": 1}`
Mesh takes `{"Mesh": 1}`
GameServer takes `{"PlaceId": 1, "TeamCreate": false}`

To kill games, use `/StopGame`. It takes `{"pid": 67}`.