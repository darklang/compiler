(* TypeIdentity.mli - Declarations with the same content are one type. *)
val unify :
  bool ->
  WrittenTypeSupport.typeInventory ->
  StringOrder.Set.t ->
  WrittenTypeSupport.typeInventory ->
  WrittenSource.item list ->
  WrittenTypeSupport.typeInventory * WrittenSource.item list
