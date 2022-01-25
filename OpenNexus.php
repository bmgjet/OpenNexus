<?php
//Setup Key that needs to be provided as user-agent for php script to react
$Key = 'bmgjet123';
error_reporting(0);
//Checks DataFolders Exsists
if (!file_exists($_SERVER['DOCUMENT_ROOT'] . '/OpenNexus/')) {
    mkdir($_SERVER['DOCUMENT_ROOT'] . '/OpenNexus/', 0777, true);
}
if (!file_exists($_SERVER['DOCUMENT_ROOT'] . '/OpenNexus/Packets/')) {
    mkdir($_SERVER['DOCUMENT_ROOT'] . '/OpenNexus/Packets/', 0777, true);
}

if (!file_exists($_SERVER['DOCUMENT_ROOT'] . '/OpenNexus/Old/')) {
    mkdir($_SERVER['DOCUMENT_ROOT'] . '/OpenNexus/Old/', 0777, true);
}

if (!file_exists($_SERVER['DOCUMENT_ROOT'] . '/OpenNexus/Sync/')) {
    mkdir($_SERVER['DOCUMENT_ROOT'] . '/OpenNexus/Sync/', 0777, true);
}

//Make sure its a POST Event and not a GET
if ($_SERVER['REQUEST_METHOD'] === 'POST') 
{
//get POST data
$entityBody = file_get_contents('php://input');

//Check useragent provided matches the key
$file = isset($_SERVER['HTTP_USER_AGENT']) ? $_SERVER['HTTP_USER_AGENT'] : null;
if($file != $Key) { echo 'invalid key'; return;}

//Setup Variables
$TARGET = '';
$FROM = '';
$Command = '';
$headers = apache_request_headers();
foreach ($headers as $header => $value) 
{
	if($header == 'TARGET') {$TARGET = $value;}
	if($header == 'FROM') {$FROM = $value;}
	if($header == 'CMD') {$Command = $value;}
}
//Check data has a Target and From IP-Port
if($TARGET == '' || $FROM == '') { echo 'Bad Data'; return;}

//Create time stamp
$date = date_create();

//Ping Request to Sync Servers
if($Command == 'PING') 
{ 
//Create sync file from ip/port
file_put_contents($_SERVER['DOCUMENT_ROOT'] . '/OpenNexus/Sync/'.$FROM, $entityBody);
//delay 100ms for file io
usleep(100000);
//if less then 2 sync files dont sync yet
$amount = glob($_SERVER['DOCUMENT_ROOT'] . '/OpenNexus/Sync/' . "*", GLOB_NOSORT);
if(count($amount) < 2) { echo 'WAIT'; return;}
//Create a time stamp
if (!file_exists($_SERVER['DOCUMENT_ROOT'] . '/OpenNexus/Sync/TimeStamp.txt')) 
{
file_put_contents($_SERVER['DOCUMENT_ROOT'] . '/OpenNexus/Sync/TimeStamp.txt', date_timestamp_get($date));
usleep(100000);
}
//echos timestamp from file + servers time stamp to work out a time to sync at.
include($_SERVER['DOCUMENT_ROOT'] . '/OpenNexus/Sync/TimeStamp.txt');
echo ' ' . date_timestamp_get($date);
return;
}

//Removes time stamp file
if (file_exists($_SERVER['DOCUMENT_ROOT'] . '/OpenNexus/Sync/TimeStamp.txt')) 
{
unlink($_SERVER['DOCUMENT_ROOT'] . '/OpenNexus/Sync/TimeStamp.txt');
return;
}

//Remove sync file
if($Command == 'LEAVE') 
{ 
if (file_exists($_SERVER['DOCUMENT_ROOT'] . '/OpenNexus/Sync/'.$FROM)) 
{
unlink($_SERVER['DOCUMENT_ROOT'] . '/OpenNexus/Sync/'.$FROM);
return;
}
}

//Fill sync file with current ferry status
if($Command == 'SYNC') 
{ 
file_put_contents($_SERVER['DOCUMENT_ROOT'] . '/OpenNexus/Sync/'.$FROM, $entityBody);
return;
}

//Checks from and target sync files have same status
if($Command == 'READY') 
{ 
if(file_get_contents($_SERVER['DOCUMENT_ROOT'] . '/OpenNexus/Sync/'.$FROM) === file_get_contents($_SERVER['DOCUMENT_ROOT'] . '/OpenNexus/Sync/'.$TARGET))
{
	echo 'true';
	return;
}
echo 'false';
return;
}

//Store a entity packet
if($Command == 'WRITE') 
{ 
if($entityBody != "")
{
file_put_contents($_SERVER['DOCUMENT_ROOT'] . '/OpenNexus/Packets/' . $TARGET . '-' . date_timestamp_get($date), $entityBody);
return;
}
}

//Find files that this server wants
$matches = glob($_SERVER['DOCUMENT_ROOT'] . '/OpenNexus/Packets/' . $FROM .'*', GLOB_NOSORT);
//Sort so newest listed first
array_multisort(array_map('filemtime', $matches), SORT_NUMERIC, SORT_DESC, $matches);
foreach ($matches as $filen) 
{
	//Reply with newest file
	include $filen;
	//Move file to old filder
	$source_file = $filen;
	$destination_path = $_SERVER['DOCUMENT_ROOT'] . '/OpenNexus/Old/';
	rename($source_file, $destination_path . pathinfo($source_file, PATHINFO_BASENAME));
	//End function
	return;
}
}
?>