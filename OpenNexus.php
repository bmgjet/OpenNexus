<?php
//Setup Key that needs to be provided as user-agent for php script to react
$Key = 'bmgjet123';

//Checks DataFolders Exsists
if (!file_exists($_SERVER['DOCUMENT_ROOT'] . '/ON/')) {
    mkdir($_SERVER['DOCUMENT_ROOT'] . '/ON/', 0777, true);
}
if (!file_exists($_SERVER['DOCUMENT_ROOT'] . '/ON-SENT/')) {
    mkdir($_SERVER['DOCUMENT_ROOT'] . '/ON-SENT/', 0777, true);
}

//Make sure its a POST Event and not a GET
if ($_SERVER['REQUEST_METHOD'] === 'POST') 
{
//get POST data
$entityBody = file_get_contents('php://input');

//Make sure its base64 for some protection against code injection (Only Supports Compression Mode)
if (!preg_match('/^[a-zA-Z0-9\/\r\n+]*={0,2}$/', $entityBody)){echo 'Bad data'; return;}

//Check useragent provided matches the key
$file = isset($_SERVER['HTTP_USER_AGENT']) ? $_SERVER['HTTP_USER_AGENT'] : null;
if($file != $Key) { echo 'invalid key'; return;}

//Setup Variables
$TARGET = '';
$FROM = '';
$headers = apache_request_headers();
foreach ($headers as $header => $value) 
{
	if($header == 'TARGET') {$TARGET = $value;}
	if($header == 'FROM') {$FROM = $value;}
}
//Check data has a Target and From IP-Port
if($TARGET == '' || $FROM == '') { echo 'Bad Data'; return;}

//Ping Request to Sync Servers
if($entityBody == 'PING') 
{ 
//Create Sync Data
if (!file_exists($_SERVER['DOCUMENT_ROOT'] . '/ON-SYNC/')) {
    mkdir($_SERVER['DOCUMENT_ROOT'] . '/ON-SYNC/', 0777, true);
}
//Create sync file from ip/port
if (!file_exists($_SERVER['DOCUMENT_ROOT'] . '/ON-SYNC/'.$FROM)) {
file_put_contents($_SERVER['DOCUMENT_ROOT'] . '/ON-SYNC/'.$FROM, $entityBody);
}
//if less then 2 sync files dont sync yet
$amount = count(glob($_SERVER['DOCUMENT_ROOT'] . '/ON-SYNC/' . "*"));
if($amount < 2) { echo 'WAIT'; return;}
//send 10 sec delay before departing
echo '10';
return;
}

//Remove Sync Data
$oldfiles = glob($_SERVER['DOCUMENT_ROOT'] . '/ON-SYNC/' . "*");
foreach($oldfiles as $kill)
{
  if(is_file($kill)) 
  {
    unlink($kill);
  }
}


//Create time stamp
$date = date_create();

//Create file
$filepath = $_SERVER['DOCUMENT_ROOT'] . '/ON/' . $TARGET . '-' . date_timestamp_get($date);
file_put_contents($filepath, $entityBody);

//Sleep for 100ms to allow files to read/write
usleep(100000);

//Fild files that this server wants
$matches = glob(realpath('./ON') . '/'. $FROM .'*', GLOB_NOSORT);
//Sort so newest listed first
array_multisort(array_map('filemtime', $matches), SORT_NUMERIC, SORT_DESC, $matches);
foreach ($matches as $filen) 
{
	//Reply with newest file
	include $filen;
	//Move file to old filder
	$source_file = $filen;
	$destination_path = $_SERVER['DOCUMENT_ROOT'] . '/ON-SENT/';
	rename($source_file, $destination_path . pathinfo($source_file, PATHINFO_BASENAME));
	//End function
	return;
}
//End of POST
return;
}
?>