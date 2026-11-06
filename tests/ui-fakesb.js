// A fake of the Supabase endpoints the launcher uses, for the accounts UI test. Behaves like the real service: email codes, unique usernames, row-level access.
// in-page fake of the Supabase auth + rest endpoints the launcher uses
(function(){
  var DB={users:{},profiles:{},codes:{},tok:{},n:0,confirm:true,online:true,log:[]};window.FSB=DB;
  function uid(){DB.n++;return "u"+DB.n+"-0000";}
  function sess(u){var at="at"+Math.random(),rt="rt"+Math.random();DB.tok[at]=u.id;DB.tok[rt]=u.id;return {access_token:at,refresh_token:rt,expires_in:3600,user:{id:u.id,email:u.email,email_confirmed_at:u.conf?"x":null,user_metadata:{username:u.meta.username},identities:[{}]}};}
  function byEmail(e){for(var k in DB.users)if(DB.users[k].email===e)return DB.users[k];return null;}
  function me(b){return DB.users[DB.tok[b]];}
  window.external.Supa=function(id,m,path,body,bearer){
    var j=body?JSON.parse(body):{};DB.log.push(m+" "+path);
    setTimeout(function(){
      function out(st,o){onHttp(id,st>=200&&st<300?"1":"0",JSON.stringify(o),String(st));}
      if(!DB.online){onHttp(id,"0","Unable to connect","0");return;}
      if(path==="/auth/v1/signup"&&DB.slowMail&&!byEmail(j.email)){var us={id:uid(),email:j.email,pw:j.password,meta:j.data,conf:false};DB.users[us.id]=us;DB.profiles[us.id]={username:j.data.username,friend_code:null};DB.codes[j.email]="123456";out(504,{code:504,error_code:"request_timeout",msg:"Processing this request timed out, please retry after a moment."});return;}
      if(path==="/auth/v1/signup"){var ex=byEmail(j.email);if(ex){out(200,{id:"fake",email:j.email,identities:[]});return;}
        if(!/^[A-Za-z0-9_]{3,20}$/.test(j.data.username))j.data.username="player_x";
        for(var q in DB.profiles)if(DB.profiles[q].username.toLowerCase()===j.data.username.toLowerCase()){out(500,{msg:"Database error saving new user"});return;}
        var u={id:uid(),email:j.email,pw:j.password,meta:j.data,conf:!DB.confirm};DB.users[u.id]=u;DB.profiles[u.id]={username:j.data.username,friend_code:null};
        if(DB.confirm){DB.codes[j.email]="123456";out(200,{id:u.id,email:u.email,identities:[{}]});}else out(200,sess(u));return;}
      if(path.indexOf("/auth/v1/token?grant_type=password")===0){var u2=byEmail(j.email);if(!u2||u2.pw!==j.password){out(400,{error_code:"invalid_credentials",msg:"Invalid login credentials"});return;}
        if(!u2.conf){out(400,{error_code:"email_not_confirmed",msg:"Email not confirmed"});return;}out(200,sess(u2));return;}
      if(path.indexOf("/auth/v1/token?grant_type=refresh_token")===0){var u3=DB.users[DB.tok[j.refresh_token]];if(!u3){out(400,{error_code:"refresh_token_not_found"});return;}out(200,sess(u3));return;}
      if(path==="/auth/v1/verify"){var u4=byEmail(j.email);if(!u4||DB.codes[j.email]!==j.token||j.type==="email"&&false){out(403,{error_code:"otp_expired",msg:"Token has expired or is invalid"});return;}
        if(j.type==="signup"&&DB.rejectSignupType){out(403,{error_code:"otp_expired",msg:"Token has expired or is invalid"});return;}
        delete DB.codes[j.email];u4.conf=true;out(200,sess(u4));return;}
      if(path==="/auth/v1/resend"||path==="/auth/v1/recover"){if(DB.notAuthorized){out(400,{error_code:"email_address_not_authorized",msg:"Email address not authorized"});return;}DB.codes[j.email]="654321";out(200,{});return;}
      if(path==="/auth/v1/user"&&m==="PUT"){var u5=me(bearer);if(!u5){out(401,{});return;}u5.pw=j.password;out(200,{id:u5.id});return;}
      if(path==="/auth/v1/logout"){out(204,{});return;}
      if(path==="/rest/v1/rpc/username_available"){var free=/^[A-Za-z0-9_]{3,20}$/.test(j.name);for(var p in DB.profiles)if(DB.profiles[p].username.toLowerCase()===j.name.toLowerCase())free=false;out(200,free);return;}
      if(path==="/rest/v1/rpc/find_player"){if(!me(bearer)){out(401,{});return;}var r=[];for(var p2 in DB.profiles){var pr=DB.profiles[p2];if(pr.username.toLowerCase()===j.name.toLowerCase()&&pr.friend_code)r.push({username:pr.username,friend_code:pr.friend_code});}out(200,r);return;}
      if(path==="/rest/v1/rpc/delete_my_account"){var u6=me(bearer);if(!u6){out(401,{});return;}delete DB.users[u6.id];delete DB.profiles[u6.id];out(204,null);return;}
      if(path.indexOf("/rest/v1/profiles")===0){var u7=me(bearer);if(!u7){out(401,{});return;}
        if(m==="GET"){out(200,[DB.profiles[u7.id]]);return;}
        if(m==="PATCH"){if(j.username){for(var p3 in DB.profiles)if(p3!==u7.id&&DB.profiles[p3].username.toLowerCase()===j.username.toLowerCase()){out(409,{code:"23505",message:"duplicate key value violates unique constraint"});return;}DB.profiles[u7.id].username=j.username;}
          if(j.friend_code)DB.profiles[u7.id].friend_code=j.friend_code;out(204,null);return;}}
      out(404,{msg:"not faked: "+path});
    },30);
    return "ok";};
})();
