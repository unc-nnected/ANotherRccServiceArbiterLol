# ANotherRccServiceArbiterLol

ANotherRccServiceArbiterLol (ANRSAL for short) is an arbiter designed to make gameservers and renders with 2008E-2017E RCCService software.

```
--dir "path" | path to RCCService directory, this one is required (DUH!)

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
--verbose | verbose logging
--experimental | experimental features

example launch: ANRSAL.exe --dir "C:\ANRSAL\RCCService" --port 8124 --cores 12 --secret "key" --gscript "C:\ANRSAL\Scripts\gameserver.lua" --rscript "C:\ACCService\Scripts\render.lua" --verbose
```

To interact with the arbiter, here are the API endpoints:

`/StartGame?type={GameServer, Avatar, Mesh, Model, Place}` is a POST endpoint, here are some POST requests:

Place takes `{"PlaceId": 1}`

Avatar takes `{"UserId": 1, "IsHeadshot": false, "IsClothing": false}`

Model takes `{"AssetId": 1}`

Mesh takes `{"Mesh": 1}`

GameServer takes `{"PlaceId": 1, "TeamCreate": false}`.

To kill running jobs, use POST `/StopGame`. It takes `{"pid": 67}`.

To get all jobs from an running RCCService, use GET `/GetAllJobs?port={SOAP Port}&limit={List limited amount of jobs}`.

To get a specific Job, use GET `/GetJob/{jobId}`.