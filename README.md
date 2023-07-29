<h2>Open Nexus: (WIP)</h2>

<p><strong>Requires:</strong><br />
MySQL 5.7.37 (since version 8 will throw password error)</p>

<p><strong>What Transferes:</strong><br />
BasePlayer - Stats, Inventory, Modifiers, Blueprints, Skins, Keys, Photos, Cassettes<br />
BaseVehicle - Stats, Inventory, Seats, Bags, Modules<br />
Parented - Entities, skins, items<br />
Plugins - Backpacks, Inventory, Economics Balance, ServerRewards Points, ZLevels<br />
Server - DateTime, Weather, Lock player to 1 server at time</p>

<p><strong>Database setup: </strong><br />
Create a database, Create a username/password to use with it.<br />
Enter these settings into the config file and restart plugin. It will create the tables on its own. And the error messages should stop.<br />
You have to use standard type password since thats all the Oxide.Mysql supports.<br />
<br />
  
<strong>Plugin setup:</strong><br />
Default settings should work best but theres other adjustments provided within the config file.</p>

<p><strong>Chat Commands:</strong><br />
OpenNexus.reloadconfig - Reloads the config file.<br />
OpenNexus.resetconfig - Resets config back to default.<br />
OpenNexus.debug - Toggles config debug settings to see console output.<br />
<br />
<strong>Permissions: </strong><br />
Giving users permission &quot;OpenNexus.bypass&quot; will allow them to join other servers on the open nexus when they already have a body on one. Other wise by default no matter which one they join they will spawn on last server they were on.<br />
<br />
<strong>Map Setup</strong>:<br />
<u>DONT PLACE THE &quot;Islandmarker&quot;</u><br />
<br />
You need to place down a &quot;<a class="js-navigation-open Link--primary" href="https://github.com/bmgjet/OpenNexus/blob/main/OpenNexusDock.prefab" title="OpenNexusDock.prefab">OpenNexusDock.prefab&quot;</a> in a map editor such as RustEdit.<br />
Line it up in 90 degree increments only (N,E,S,W)<br />
Place down a Nexusferry Entity and prefab group it with the layout below<br />
<br />
SERVER=ipaddress,PORT=portnumber,name<br />
<br />
Replace ipaddress with the IP address of the server you want to be redirected to.<br />
Replace portnumber with the port number of that server.<br />
Enter what ever you want in the name as long as its different from any other Docks placed on the nexus servers.<br />
Make sure you tick &quot;Convert Selection To Group&quot;<br />
<br />
Make sure you have the ferry height (y) as 0. And then line up the OpenNexusDock with the ferry to get the height right.<br />
Video of setting up in rustedit: https://youtu.be/87J-te6S6cg
  <br />
<br />
If you want to add a Nexus Island in the distance you can.<br />
For this to work you need to have it with in the radius of the map.<br />
Such as if you use 4000 size map. Then you can only go to a max of 2000 or -2000 on X or Z axis.<br />
If it goes outside of this it will just be invisable. Its image is offset from the actual position you can can rotate it to have it show a bit further away.<br />
You should also drop its height down a bit with the Y axis so it doesnt look like its floating in the disatance -18f is a starting good value.<br />
<img alt="IslandScreenShot" src="https://i.ibb.co/W6b6T48/Island-Screenshot.jpg" /></p>

<p><strong>Hooks:</strong><br />
OnOpenNexusRead(string packet) - return a string to replace packet, Return a bool to cancel function.<br />
OnOpenNexusWrite(string packet) - return a string to replace packet, Return a bool to cancel function.<br />
<br />
<strong>More:</strong><br />
Heres a longish video from testing stages of it showing everything transferring over.</p>

<p><strong>Video link</strong></p>

<p><a class="style-scope ytcp-video-info" href="https://youtu.be/PM8-ObRGV1E" rel="noopener" target="_blank">https://youtu.be/PM8-ObRGV1E </a></p>
