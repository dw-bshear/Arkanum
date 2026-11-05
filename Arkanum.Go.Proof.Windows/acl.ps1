# Dot-source only from an elevated install. Replaces, rather than extends, DACLs.
function Assert-ProofTrustedTree([string]$Path) {
    # Fixed service principal, never a caller-provided SID. This is the service-name SID,
    # not a user identity. No recursive traversal or privileged ACL rewrite before trust.
    $trusted=@('S-1-5-18','S-1-5-32-544','S-1-5-80-4248138865-3867576754-632203184-3390250742-602101466')
    $write=[Security.AccessControl.FileSystemRights]'Write,Delete,DeleteSubdirectoriesAndFiles,ChangePermissions,TakeOwnership'
    $pending=New-Object 'Collections.Generic.Queue[string]'
    $pending.Enqueue($Path)
    while($pending.Count -gt 0) {
        $current=$pending.Dequeue()
        $item=Get-Item -LiteralPath $current -Force -ErrorAction Stop
        if($item.Attributes -band [IO.FileAttributes]::ReparsePoint) {throw 'Reparse points forbidden in proof tree'}
        $acl=Get-Acl -LiteralPath $current -ErrorAction Stop
        $owner=$acl.GetOwner([Security.Principal.SecurityIdentifier]).Value
        if($trusted -notcontains $owner) {throw 'Untrusted proof tree owner; refusing privileged traversal'}
        foreach($rule in $acl.GetAccessRules($true,$true,[Security.Principal.SecurityIdentifier])) {
            if($rule.AccessControlType -eq [Security.AccessControl.AccessControlType]::Allow -and
               !($rule.PropagationFlags -band [Security.AccessControl.PropagationFlags]::InheritOnly) -and
               ($rule.FileSystemRights -band $write) -and $trusted -notcontains $rule.IdentityReference.Value) {
                throw 'Untrusted proof tree writer; refusing privileged traversal'
            }
        }
        # A null DACL is Everyone full control; an empty protected DACL is safe but unusable.
        if($null -eq $acl.GetSecurityDescriptorBinaryForm()) {throw 'Unreadable proof tree security'}
        $raw=New-Object Security.AccessControl.RawSecurityDescriptor($acl.GetSecurityDescriptorSddlForm('All'))
        if($null -eq $raw.DiscretionaryAcl) {throw 'Null proof tree DACL forbidden'}
        if($item.PSIsContainer) {
            foreach($child in Get-ChildItem -LiteralPath $current -Force -ErrorAction Stop) {$pending.Enqueue($child.FullName)}
        }
    }
}
function Set-ProofAcl([string]$Path,[bool]$ReadUsers) {
    Assert-ProofTrustedTree $Path
    $items=@(Get-Item -LiteralPath $Path -Force)+@(Get-ChildItem -LiteralPath $Path -Recurse -Force)
    foreach($item in $items) {
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Reparse points forbidden in proof tree' }
    }
    foreach($item in $items) {
        if($item.PSIsContainer) {
            $acl=New-Object Security.AccessControl.DirectorySecurity
            $inherit=[Security.AccessControl.InheritanceFlags]'ContainerInherit,ObjectInherit'
        } else {
            $acl=New-Object Security.AccessControl.FileSecurity
            $inherit=[Security.AccessControl.InheritanceFlags]::None
        }
        $acl.SetAccessRuleProtection($true,$false)
        $acl.SetOwner((New-Object Security.Principal.SecurityIdentifier('S-1-5-32-544')))
        foreach($principal in @('S-1-5-18','S-1-5-32-544','NT SERVICE\ArkanumGoSyncProof')) {
            if($principal.StartsWith('S-1-')) {$identity=New-Object Security.Principal.SecurityIdentifier($principal)}
            else {$identity=New-Object Security.Principal.NTAccount($principal)}
            $rights='FullControl'
            if($ReadUsers -and $principal -ceq 'NT SERVICE\ArkanumGoSyncProof') {$rights='ReadAndExecute'}
            $rule=New-Object Security.AccessControl.FileSystemAccessRule($identity,$rights,$inherit,'None','Allow')
            $acl.AddAccessRule($rule)
        }
        if($ReadUsers) {
            $identity=New-Object Security.Principal.SecurityIdentifier('S-1-5-32-545')
            $rule=New-Object Security.AccessControl.FileSystemAccessRule($identity,'ReadAndExecute',$inherit,'None','Allow')
            $acl.AddAccessRule($rule)
        }
        Set-Acl -LiteralPath $item.FullName -AclObject $acl
    }
}
